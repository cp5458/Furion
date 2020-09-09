﻿// -----------------------------------------------------------------------------
// Fur 是 .NET 5 平台下极易入门、极速开发的 Web 应用框架。
// Copyright © 2020 Fur, Baiqian Co.,Ltd.
//
// 框架名称：Fur
// 框架作者：百小僧
// 框架版本：1.0.0
// 源码地址：https://gitee.com/monksoul/Fur
// 开源协议：Apache-2.0（http://www.apache.org/licenses/LICENSE-2.0）
// -----------------------------------------------------------------------------

using System.Data;
using System.Linq;

namespace Fur.DatabaseAccessor
{
    /// <summary>
    /// 数据库提供器选项
    /// </summary>
    internal static class DatabaseProvider
    {
        /// <summary>
        /// SqlServer 提供器程序集
        /// </summary>
        internal const string SqlServer = "Microsoft.EntityFrameworkCore.SqlServer";

        /// <summary>
        /// Sqlite 提供器程序集
        /// </summary>
        internal const string Sqlite = "Microsoft.EntityFrameworkCore.Sqlite";

        /// <summary>
        /// Cosmos 提供器程序集
        /// </summary>
        internal const string Cosmos = "Microsoft.EntityFrameworkCore.Cosmos";

        /// <summary>
        /// 内存数据库 提供器程序集
        /// </summary>
        internal const string InMemory = "Microsoft.EntityFrameworkCore.InMemory";

        /// <summary>
        /// MySql 提供器程序集
        /// </summary>
        internal const string MySql = "Pomelo.EntityFrameworkCore.MySql";

        /// <summary>
        /// PostgreSQL 提供器程序集
        /// </summary>
        internal const string PostgreSQL = "Npgsql.EntityFrameworkCore.PostgreSQL";

        /// <summary>
        /// 不支持存储过程的数据库
        /// </summary>
        internal static readonly string[] NotSupportStoredProcedureDatabases;

        /// <summary>
        /// 不支持函数的数据库
        /// </summary>
        internal static readonly string[] NotSupportFunctionDatabases;

        /// <summary>
        /// 不支持表值函数的数据库
        /// </summary>
        internal static readonly string[] NotSupportTableFunctionDatabases;

        /// <summary>
        /// 构造函数
        /// </summary>
        static DatabaseProvider()
        {
            NotSupportStoredProcedureDatabases = new[]
            {
                Sqlite,
                InMemory
            };

            NotSupportFunctionDatabases = new[]
            {
                Sqlite,
                InMemory
            };

            NotSupportTableFunctionDatabases = new[]
            {
                Sqlite,
                InMemory,
                MySql
            };
        }

        /// <summary>
        /// 是否支持存储过程
        /// </summary>
        /// <param name="providerName">提供器名</param>
        /// <param name="commandType">命令类型</param>
        /// <returns>bool</returns>
        internal static bool IsSupportStoredProcedure(string providerName, CommandType commandType)
        {
            return commandType == CommandType.StoredProcedure && !DatabaseProvider.NotSupportStoredProcedureDatabases.Contains(providerName);
        }
    }
}