﻿// --------------------------------------------------------------------------------------
// Fur 是 .NET 5 平台下极易入门、极速开发的 Web 应用框架。
// Copyright © 2020 Fur, Baiqian Co.,Ltd.
//
// 框架名称：Fur
// 框架作者：百小僧
// 框架版本：1.0.0
// 源码地址：https://gitee.com/monksoul/Fur 
// 开源协议：Apache-2.0（https://gitee.com/monksoul/Fur/blob/alpha/LICENSE）
// --------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Fur.Extensions
{
    /// <summary>
    /// 对象拓展类
    /// </summary>
    internal static class ObjectExtensions
    {
        /// <summary>
        /// 判断是否是富基元类型
        /// </summary>
        /// <param name="type">类型</param>
        /// <returns></returns>
        internal static bool IsRichPrimitive(this Type type)
        {
            // 基元类型或值类型或字符串类型
            if (type.IsPrimitive || type.IsValueType || type == typeof(string)) return true;

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>)) return type.GenericTypeArguments[0].IsRichPrimitive();

            return false;
        }

        /// <summary>
        /// 合并两个字典
        /// </summary>
        /// <param name="dic">字典</param>
        /// <param name="newDic">新字典</param>
        /// <returns></returns>
        internal static Dictionary<string, string> AddOrUpdate(this Dictionary<string, string> dic, Dictionary<string, string> newDic)
        {
            foreach (var key in newDic.Keys)
            {
                if (dic.ContainsKey(key))
                    dic[key] = newDic[key];
                else
                    dic.Add(key, newDic[key]);
            }

            return dic;
        }
    }
}