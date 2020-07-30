﻿using Autofac;
using Fur.ApplicationBase;
using Fur.ApplicationBase.Attributes;
using Fur.ApplicationBase.Wrappers;
using Fur.DatabaseAccessor.Entities;
using Fur.DatabaseAccessor.Entities.Configurations;
using Fur.DatabaseAccessor.Extensions;
using Fur.DatabaseAccessor.MultipleTenants;
using Fur.DatabaseAccessor.MultipleTenants.Entities;
using Fur.DatabaseAccessor.MultipleTenants.Options;
using Fur.DatabaseAccessor.MultipleTenants.Providers;
using Fur.DatabaseAccessor.Options;
using Fur.TypeExtensions;
using Mapster;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using static Fur.TypeExtensions.TypeExtensions;

namespace Fur.DatabaseAccessor.Contexts.Staters
{
    /// <summary>
    /// Fur 数据库上下文状态器
    /// <para>解决 <see cref="FurDbContext{TDbContext, TDbContextLocator}"/> 重复初始化问题</para>
    /// </summary>
    [NonWrapper]
    internal static class FurDbContextStater
    {
        /// <summary>
        /// 包含数据库实体定义类型
        /// <para>包含所有继承 <see cref="IDbEntityBase"/> 或 <see cref="IDbEntityConfigure"/> 类型</para>
        /// </summary>
        private static readonly ConcurrentBag<Type> _includeDbEntityDefinedTypes;

        /// <summary>
        /// 数据库函数定义方法集合
        /// </summary>
        private static readonly ConcurrentBag<MethodWrapper> _dbFunctionMethods;

        /// <summary>
        /// 模型构建器泛型 <see cref="ModelBuilder.Entity{TEntity}"/> 方法
        /// </summary>
        private static readonly MethodInfo _modelBuilderEntityMethod;

        /// <summary>
        /// 构造函数
        /// </summary>
        static FurDbContextStater()
        {
            _includeDbEntityDefinedTypes ??= new ConcurrentBag<Type>(
                AppGlobal.Application.PublicClassTypeWrappers
                .Where(u => u.CanBeNew && (typeof(IDbEntityBase).IsAssignableFrom(u.Type) || typeof(IDbEntityConfigure).IsAssignableFrom(u.Type)))
                .Distinct()
                .Select(u => u.Type));

            if (_includeDbEntityDefinedTypes.Count > 0)
            {
                _modelBuilderEntityMethod ??= typeof(ModelBuilder)
                    .GetMethods()
                    .FirstOrDefault(u => u.Name == nameof(ModelBuilder.Entity) && u.IsGenericMethod && u.GetParameters().Length == 0);
            }

            _dbFunctionMethods = new ConcurrentBag<MethodWrapper>(
                AppGlobal.Application.PublicMethodWrappers
                .Where(u => u.IsStaticMethod && u.Method.IsDefined(typeof(Attributes.DbFunctionAttribute)) && u.ThisDeclareType.IsAbstract && u.ThisDeclareType.IsSealed));
        }

        /// <summary>
        /// 扫描数据库对象类型加入模型构建器中
        /// <para>包括视图、存储过程、函数（标量函数/表值函数）初始化、及种子数据、查询筛选器配置</para>
        /// </summary>
        /// <param name="modelBuilder">模型上下文</param>
        /// <param name="dbContextLocatorType">数据库上下文定位器</param>
        /// <param name="dbContext">数据库上下文</param>
        internal static void ScanDbObjectsToBuilding(ModelBuilder modelBuilder, Type dbContextLocatorType, DbContext dbContext)
        {
            // 查找当前上下文相关联的类型
            var dbContextEntityTypes = _includeDbEntityDefinedTypes.Where(u => IsThisDbContextEntityType(u, dbContextLocatorType));
            if (dbContextEntityTypes.Count() == 0) goto DbFunctionConfigure;

            var dbContextType = dbContext.GetType();
            var hasDbContextQueryFilter = typeof(IDbContextQueryFilter).IsAssignableFrom(dbContextType);

            // 注册基于Schema架构的多租户模式

            IMultipleTenantOnSchemaProvider multipleTenantOnSchemaProvider = default;
            if (AppGlobal.SupportedMultipleTenant && dbContextLocatorType != typeof(FurMultipleTenanDbContextLocator) && AppGlobal.MultipleTenantConfigureOptions == FurMultipleTenantOptions.OnSchema)
            {
                var lifetimeScope = dbContext.GetService<ILifetimeScope>();
                multipleTenantOnSchemaProvider = lifetimeScope.Resolve<IMultipleTenantOnSchemaProvider>();
            }

            foreach (var dbEntityType in dbContextEntityTypes)
            {
                EntityTypeBuilder entityTypeBuilder = default;

                // 配置数据库无键实体
                DbNoKeyEntityConfigure(dbEntityType, modelBuilder, dbContextLocatorType, multipleTenantOnSchemaProvider, ref entityTypeBuilder);

                // 配置数据库实体类型构建器
                DbEntityTypeBuilderConfigure(dbEntityType, modelBuilder, dbContextLocatorType, multipleTenantOnSchemaProvider, ref entityTypeBuilder);

                // 配置数据库种子数据
                DbSeedDataConfigure(dbEntityType, modelBuilder, dbContext, dbContextLocatorType, multipleTenantOnSchemaProvider, ref entityTypeBuilder);

                // 配置数据库上下文查询筛选器
                DbContextQueryFilterConfigure(dbContext, dbContextType, modelBuilder, dbEntityType, hasDbContextQueryFilter, multipleTenantOnSchemaProvider, ref entityTypeBuilder);

                // 配置数据库查询筛选器
                DbQueryFilterConfigure(dbEntityType, modelBuilder, dbContext, dbContextLocatorType, multipleTenantOnSchemaProvider, ref entityTypeBuilder);

                // 配置模型类型构建器
                CreateDbEntityTypeBuilderIfNull(modelBuilder, dbEntityType, multipleTenantOnSchemaProvider, ref entityTypeBuilder);
            }

            // 配置数据库函数
            DbFunctionConfigure: DbFunctionConfigure(modelBuilder, dbContextLocatorType);
        }

        /// <summary>
        /// 配置数据库无键实体
        /// <para>只有继承 IDbNoKeyEntity 接口的类型才是数据库无键实体</para>
        /// </summary>
        /// <param name="dbEntityType">数据库实体类型</param>
        /// <param name="modelBuilder">模型构建器</param>
        /// <param name="dbContextLocatorType">数据库上下文定位器</param>
        /// <param name="entityTypeBuilder">实体类型构建器</param>
        private static void DbNoKeyEntityConfigure(Type dbEntityType, ModelBuilder modelBuilder, Type dbContextLocatorType, IMultipleTenantOnSchemaProvider multipleTenantOnSchemaProvider, ref EntityTypeBuilder entityTypeBuilder)
        {
            if (!typeof(IDbNoKeyEntity).IsAssignableFrom(dbEntityType)) return;

            var dbNoKeyEntityGenericArguments = dbEntityType.GetTypeGenericArguments(typeof(IDbNoKeyEntity), GenericArgumentSourceOptions.BaseType);
            if (dbNoKeyEntityGenericArguments != null)
            {
                var dbContextLocators = dbNoKeyEntityGenericArguments.Skip(1);

                if (CheckIsInDbContextLocators(dbContextLocators, dbContextLocatorType))
                {
                    CreateDbEntityTypeBuilderIfNull(modelBuilder, dbEntityType, multipleTenantOnSchemaProvider, ref entityTypeBuilder);

                    entityTypeBuilder.HasNoKey();
                    entityTypeBuilder.ToView((Activator.CreateInstance(dbEntityType) as IDbNoKeyEntity).DB_DEFINED_NAME);
                }
            }
        }

        /// <summary>
        /// 配置数据库实体类型构建器
        /// <para>该配置会在所有配置接口之前运行，确保后续可复写</para>
        /// </summary>
        /// <param name="dbEntityType">数据库实体类型</param>
        /// <param name="modelBuilder">模型构建器</param>
        /// <param name="dbContextLocatorType">数据库上下文定位器</param>
        /// <param name="entityTypeBuilder">实体类型构建器</param>
        private static void DbEntityTypeBuilderConfigure(Type dbEntityType, ModelBuilder modelBuilder, Type dbContextLocatorType, IMultipleTenantOnSchemaProvider multipleTenantOnSchemaProvider, ref EntityTypeBuilder entityTypeBuilder)
        {
            var dbEntityBuilderGenericArguments = dbEntityType.GetTypeGenericArguments(typeof(IDbEntityBuilder), GenericArgumentSourceOptions.Interface);
            if (dbEntityBuilderGenericArguments != null)
            {
                var dbContextLocators = dbEntityBuilderGenericArguments.Skip(1);
                if (CheckIsInDbContextLocators(dbContextLocators, dbContextLocatorType))
                {
                    CreateDbEntityTypeBuilderIfNull(modelBuilder, dbEntityBuilderGenericArguments.First(), multipleTenantOnSchemaProvider, ref entityTypeBuilder);

                    entityTypeBuilder = dbEntityType.CallMethod(
                        nameof(IDbEntityBuilder<IDbEntityBase>.HasEntityBuilder),
                        Activator.CreateInstance(dbEntityType),
                        entityTypeBuilder
                    ) as EntityTypeBuilder;
                }
            }
        }

        /// <summary>
        /// 配置数据库种子数据
        /// </summary>
        /// <param name="dbEntityType">数据库实体类型</param>
        /// <param name="modelBuilder">模型构建器</param>
        /// <param name="dbContext">数据库上下文</param>
        /// <param name="dbContextLocatorType">数据库上下文构建器</param>
        /// <param name="entityTypeBuilder">实体类型构建器</param>
        private static void DbSeedDataConfigure(Type dbEntityType, ModelBuilder modelBuilder, DbContext dbContext, Type dbContextLocatorType, IMultipleTenantOnSchemaProvider multipleTenantOnSchemaProvider, ref EntityTypeBuilder entityTypeBuilder)
        {
            if (typeof(IDbNoKeyEntity).IsAssignableFrom(dbEntityType)) return;

            var dbSeedDataGenericArguments = dbEntityType.GetTypeGenericArguments(typeof(IDbSeedData), GenericArgumentSourceOptions.Interface);

            if (dbSeedDataGenericArguments != null)
            {
                var dbContextLocators = dbSeedDataGenericArguments.Skip(1);

                if (CheckIsInDbContextLocators(dbContextLocators, dbContextLocatorType))
                {
                    CreateDbEntityTypeBuilderIfNull(modelBuilder, dbSeedDataGenericArguments.First(), multipleTenantOnSchemaProvider, ref entityTypeBuilder);

                    var seedDatas = dbEntityType.CallMethod(
                        nameof(IDbSeedData<IDbEntityBase>.HasData),
                        Activator.CreateInstance(dbEntityType),
                        dbContext
                    ).Adapt<IEnumerable<object>>();

                    if (seedDatas == null && !seedDatas.Any()) return;

                    entityTypeBuilder.HasData(seedDatas);
                }
            }
        }

        /// <summary>
        /// 配置数据库查询筛选器
        /// </summary>
        /// <param name="dbEntityType">数据库实体类型</param>
        /// <param name="modelBuilder">模型构建器</param>
        /// <param name="dbContext">数据库上下文</param>
        /// <param name="dbContextLocatorType">数据库上下文定位器</param>
        /// <param name="entityTypeBuilder">实体类型构建器</param>
        private static void DbQueryFilterConfigure(Type dbEntityType, ModelBuilder modelBuilder, DbContext dbContext, Type dbContextLocatorType, IMultipleTenantOnSchemaProvider multipleTenantOnSchemaProvider, ref EntityTypeBuilder entityTypeBuilder)
        {
            // 租户表无需参与过滤器
            if (dbEntityType == typeof(Tenant)) return;

            var dbQueryFilterGenericArguments = dbEntityType.GetTypeGenericArguments(typeof(IDbQueryFilter), GenericArgumentSourceOptions.Interface);
            if (dbQueryFilterGenericArguments != null)
            {
                var dbContextLocators = dbQueryFilterGenericArguments.Skip(1);
                if (CheckIsInDbContextLocators(dbContextLocators, dbContextLocatorType))
                {
                    CreateDbEntityTypeBuilderIfNull(modelBuilder, dbQueryFilterGenericArguments.First(), multipleTenantOnSchemaProvider, ref entityTypeBuilder);

                    var queryFilters = dbEntityType.CallMethod(
                        nameof(IDbQueryFilter<IDbEntityBase>.HasQueryFilter),
                        Activator.CreateInstance(dbEntityType)
                        , dbContext
                     ).Adapt<IEnumerable<LambdaExpression>>();

                    if (queryFilters == null && !queryFilters.Any()) return;

                    foreach (var queryFilter in queryFilters)
                    {
                        if (queryFilter != null) entityTypeBuilder.HasQueryFilter(queryFilter);
                    }
                }
            }
        }

        /// <summary>
        /// 配置数据库函数类型
        /// </summary>
        /// <param name="modelBuilder">模型构建器</param>
        /// <param name="dbContextLocatorType">数据库上下文定位器</param>
        private static void DbFunctionConfigure(ModelBuilder modelBuilder, Type dbContextLocatorType)
        {
            var dbFunctionMethods = _dbFunctionMethods.Where(u => IsThisDbContextDbFunction(u, dbContextLocatorType));

            foreach (var dbFunction in dbFunctionMethods)
            {
                modelBuilder.HasDbFunction(dbFunction.Method);
            }
        }

        /// <summary>
        /// 配置数据库上下文查询筛选器
        /// <para>一旦数据库上下文继承该接口，那么该数据库上下文所有的实体都将应用该查询筛选器</para>
        /// </summary>
        /// <param name="dbContext">数据库上下文</param>
        /// <param name="dbContextType">数据库上下文类型</param>
        /// <param name="modelBuilder">模型构建器</param>
        /// <param name="dbEntityType">数据库实体类型</param>
        /// <param name="hasDbContextQueryFilter">是否有数据库上下文查询筛选器</param>
        /// <param name="entityTypeBuilder">数据库实体类型构建器</param>
        private static void DbContextQueryFilterConfigure(DbContext dbContext, Type dbContextType, ModelBuilder modelBuilder, Type dbEntityType, bool hasDbContextQueryFilter, IMultipleTenantOnSchemaProvider multipleTenantOnSchemaProvider, ref EntityTypeBuilder entityTypeBuilder)
        {
            // 租户表无需参与过滤器
            if (dbEntityType == typeof(Tenant)) return;

            if (typeof(IDbEntityBase).IsAssignableFrom(dbEntityType) && hasDbContextQueryFilter)
            {
                CreateDbEntityTypeBuilderIfNull(modelBuilder, dbEntityType, multipleTenantOnSchemaProvider, ref entityTypeBuilder);

                dbContextType.CallMethod(nameof(IDbContextQueryFilter.HasQueryFilter)
                    , dbContext
                    , dbEntityType
                    , entityTypeBuilder
                 );
            }
        }

        /// <summary>
        /// 创建数据库实体类型构建器
        /// <para>只有为 null 时才会执行</para>
        /// </summary>
        /// <param name="modelBuilder"></param>
        /// <param name="dbEntityType"></param>
        /// <param name="entityTypeBuilder"></param>
        private static void CreateDbEntityTypeBuilderIfNull(ModelBuilder modelBuilder, Type dbEntityType, IMultipleTenantOnSchemaProvider multipleTenantOnSchemaProvider, ref EntityTypeBuilder entityTypeBuilder)
        {
            var isSetEntityType = entityTypeBuilder == null;
            entityTypeBuilder ??= _modelBuilderEntityMethod.MakeGenericMethod(dbEntityType).Invoke(modelBuilder, null) as EntityTypeBuilder;

            // 忽略租户Id
            if (isSetEntityType)
            {
                if (!AppGlobal.SupportedMultipleTenant)
                {
                    entityTypeBuilder.Ignore(nameof(DbEntityBase.TenantId));
                }
                else
                {
                    if (multipleTenantOnSchemaProvider != null && dbEntityType != typeof(Tenant))
                    {
                        var tableName = dbEntityType.Name;
                        var schema = multipleTenantOnSchemaProvider.GetSchema();
                        if (dbEntityType.IsDefined(typeof(TableAttribute), false))
                        {
                            var tableAttribute = dbEntityType.GetCustomAttribute<TableAttribute>();
                            tableName = tableAttribute.Name;
                            if (tableAttribute.Schema.HasValue())
                            {
                                schema = tableAttribute.Schema;
                            }
                        }
                        entityTypeBuilder.ToTable(tableName, schema);
                    }
                }
            }
        }

        /// <summary>
        /// 判断该类型是否是当前数据库上下文的实体类型或包含实体的类型
        /// </summary>
        /// <param name="dbEntityType">数据库实体类型或包含实体的类型</param>
        /// <param name="dbContextLocatorType">数据库上下文定位器</param>
        /// <returns>bool</returns>
        private static bool IsThisDbContextEntityType(Type dbEntityType, Type dbContextLocatorType)
        {
            // 判断是否启用多租户，如果不启用，则默认不解析 Tenant 类型，返回 false
            if (!AppGlobal.SupportedMultipleTenant)
            {
                if (dbEntityType == typeof(Tenant)) return false;
                var typeGenericArguments = dbEntityType.GetTypeGenericArguments(typeof(IDbEntityConfigure), GenericArgumentSourceOptions.Interface);
                if (typeGenericArguments != null && typeGenericArguments.First() == typeof(Tenant)) return false;
            }

            // 如果是实体类型
            if (typeof(IDbEntityBase).IsAssignableFrom(dbEntityType))
            {
                // 有主键实体
                if (!typeof(IDbNoKeyEntity).IsAssignableFrom(dbEntityType))
                {
                    // 如果父类不是泛型类型，则返回 true
                    if (dbEntityType.BaseType == typeof(DbEntity) || dbEntityType.BaseType == typeof(DbEntityBase) || dbEntityType.BaseType == typeof(Object)) return true;
                    // 如果是泛型类型，但数据库上下文定位器泛型参数未空或包含 dbContextLocatorType，返回 true
                    else
                    {
                        var typeGenericArguments = dbEntityType.GetTypeGenericArguments(typeof(IDbEntityBase), GenericArgumentSourceOptions.BaseType);
                        if (CheckIsInDbContextLocators(typeGenericArguments.Skip(1), dbContextLocatorType)) return true;
                    }
                }
                // 无键实体
                else
                {
                    // 如果父类不是泛型类型，则返回 true
                    if (dbEntityType.BaseType == typeof(DbNoKeyEntity)) return true;
                    // 如果是泛型类型，但数据库上下文定位器泛型参数未空或包含 dbContextLocatorType，返回 true
                    else
                    {
                        var typeGenericArguments = dbEntityType.GetTypeGenericArguments(typeof(IDbNoKeyEntity), GenericArgumentSourceOptions.BaseType);
                        if (CheckIsInDbContextLocators(typeGenericArguments, dbContextLocatorType)) return true;
                    }
                }
            }

            // 如果继承 IDbEntityConfigure，但数据库上下文定位器泛型参数未空或包含 dbContextLocatorType，返回 true
            if (typeof(IDbEntityConfigure).IsAssignableFrom(dbEntityType))
            {
                var typeGenericArguments = dbEntityType.GetTypeGenericArguments(typeof(IDbEntityConfigure), GenericArgumentSourceOptions.Interface);
                if (CheckIsInDbContextLocators(typeGenericArguments.Skip(1), dbContextLocatorType)) return true;
            }

            return false;
        }

        /// <summary>
        /// 判断该方法是否是当前数据库上下文的函数类型
        /// </summary>
        /// <param name="methodWrapper"></param>
        /// <param name="dbContextLocatorType"></param>
        /// <returns></returns>
        private static bool IsThisDbContextDbFunction(MethodWrapper methodWrapper, Type dbContextLocatorType)
        {
            var dbFunctionAttribute = methodWrapper.CustomAttributes.FirstOrDefault(u => u.GetType() == typeof(Attributes.DbFunctionAttribute)) as Attributes.DbFunctionAttribute;
            if (CheckIsInDbContextLocators(dbFunctionAttribute.DbContextLocators, dbContextLocatorType)) return true;

            return false;
        }

        /// <summary>
        /// 检查当前数据库上下文定位器是否在指定的数据库上下文定位器集合中
        /// </summary>
        /// <param name="dbContextLocators">数据库上下文定位器集合</param>
        /// <param name="dbContextLocatorType">数据库上下文定位器</param>
        /// <returns>bool</returns>
        private static bool CheckIsInDbContextLocators(IEnumerable<Type> dbContextLocators, Type dbContextLocatorType)
        {
            return dbContextLocators == null || dbContextLocators.Count() == 0 || dbContextLocators.Contains(dbContextLocatorType);
        }
    }
}