﻿using Autofac;
using Fur.DatabaseAccessor.Contexts.Pool;
using Fur.DatabaseAccessor.Identifiers;
using Fur.DatabaseAccessor.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fur.DatabaseAccessor.Repositories.Multiples
{
    /// <summary>
    /// 泛型多上下文仓储实现类
    /// </summary>
    /// <typeparam name="TEntity"></typeparam>
    /// <typeparam name="TDbContextIdentifier"></typeparam>
    public partial class MultipleEFCoreRepositoryOfT<TEntity, TDbContextIdentifier> : EFCoreRepositoryOfT<TEntity>, IMultipleRepositoryOfT<TEntity, TDbContextIdentifier>
        where TEntity : class, IDbEntity, new()
        where TDbContextIdentifier : IDbContextIdentifier
    {
        #region 构造函数 + public MultipleDbContextEFCoreRepositoryOfT(ILifetimeScope lifetimeScope,IDbContextPool dbContextPool)

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="serviceProvider">服务提供器</param>
        /// <param name="dbContextPool">数据库上下文池</param>
        public MultipleEFCoreRepositoryOfT(
            ILifetimeScope lifetimeScope
            , IDbContextPool dbContextPool)
            : base(lifetimeScope.ResolveNamed<DbContext>(typeof(TDbContextIdentifier).Name), lifetimeScope, dbContextPool)
        {
        }

        #endregion 构造函数 + public MultipleDbContextEFCoreRepositoryOfT(ILifetimeScope lifetimeScope,IDbContextPool dbContextPool)
    }
}