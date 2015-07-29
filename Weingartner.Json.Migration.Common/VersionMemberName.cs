﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Weingartner.Json.Migration.Common
{
    public class VersionedMethod
    {
        public int Version { get; }

        public MethodInfo Method { get; }

        public VersionedMethod(int version, MethodInfo method)
        {
            Version = version;
            Method = method;
        }
    }

    public static class VersionMemberName
    {
        public static string VersionPropertyName => "Version";

        public static string VersionBackingFieldName => "_version";

        public static IEnumerable<string> SupportedVersionPropertyNames
        {
            get
            {
                yield return VersionPropertyName;
                yield return "<>Version";
            }
        }

        public const string MigrationMethodPrefix = "Migrate_";

        public static int GetCurrentVersion(Type type)
        {
            return GetMigrationMethodCandidates(type)
                .Select(v=>v.Version)
                .Concat(new[] { 0 })
                .Max();
        }

        private static IEnumerable<int> ParseMigrationMethodVersions(IEnumerable<string> enumerable)
        {
            var versions = enumerable
                .Select(m => m.Substring(MigrationMethodPrefix.Length))
                .Select(int.Parse)
                .ToList();

            if (versions.Count == 0)
                return versions;

            var firstVersion = versions[0];
            if(firstVersion!=1)
                throw new MigrationException($"Migrations must start with 'Migrate_1. but starts with 'Migration_{firstVersion}");

            var isConsecutive = !versions.Select((i, j) => i - j).Distinct().Skip(1).Any();
            if(!isConsecutive)
                throw new MigrationException($"Migrations must be consecutive but got versions {string.Join(", ", versions)}");

            return versions;
        }

        public static IEnumerable<VersionedMethod> GetMigrationMethodCandidates(Type type)
        {
            var v = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .Where(m => m.Name.StartsWith(MigrationMethodPrefix))
                .ToList();

            if (v.Count == 0)
                return Enumerable.Empty<VersionedMethod>();

            var versionedMethods = ParseMigrationMethodVersions(v.Select(m => m.Name))
                .Zip(v, (version, method) => new VersionedMethod(version, method))
                .OrderBy(m => m.Version)
                .ToList();

            versionedMethods
                .Select(m => new { m.Method, m.Method.ReturnType })
                .StartWith(new {Method = (MethodInfo) null, ReturnType = (Type)null })
                .Buffer(2, 1)
                .Where(l => l.Count == 2)
                .ForEach(l => VerifyMigrationMethodSignature(l[1].Method, l[0].ReturnType));

            return versionedMethods;
        }

        private static void ThrowInvalidMigrationSignature(MethodInfo method)
        {
            var builder = new StringBuilder();
            Debug.Assert(method.DeclaringType != null, "method.DeclaringType != null");
            builder.AppendLine
                ($"Migration method '{method.DeclaringType.FullName}.{method.Name}' should have the following signature:");
            builder.AppendLine
                (String.Format
                     ("private static {0} {1}({0} data, JsonSerializer serializer)",
                      typeof (JToken).FullName,
                      method.Name));
            throw new MigrationException(builder.ToString());
        }

        /// <summary>
        /// Checks the signiture of a migration method. 
        /// </summary>
        /// <param name="method"></param>
        /// <param name="jsonArgumentType">Can be null if you don't want to check the type. Otherwise it should be one of JObject or JArray</param>
        public static void VerifyMigrationMethodSignature(MethodInfo method, Type jsonArgumentType)
        {
            if (jsonArgumentType != null && !typeof (JToken).IsAssignableFrom(jsonArgumentType))
            {
                ThrowInvalidMigrationSignature(method);
            }

            if (method == null) throw new ArgumentNullException(nameof(method));

            var parameters = method.GetParameters();
            if (parameters.Length != 2)
                ThrowInvalidMigrationSignature(method);

            if (jsonArgumentType!=null && !parameters[0].ParameterType.IsAssignableFrom(jsonArgumentType))
                ThrowInvalidMigrationSignature(method);

            if (!typeof (JsonSerializer).IsAssignableFrom(parameters[1].ParameterType))
                ThrowInvalidMigrationSignature(method);

            if (!typeof (JToken).IsAssignableFrom(method.ReturnType))
                ThrowInvalidMigrationSignature(method);
        }

        public static int MaxMigrationMethodVersionUsingCecil(TypeDefinition type)
        {
            return MigrationMethodVersionsUsingCecil(type).Concat(Enumerable.Repeat(0, 1)).Max();
        }

        public static IEnumerable<int> MigrationMethodVersionsUsingCecil(TypeDefinition type)
        {
            return type.Methods
                       .Where(m => m.IsStatic && m.IsPrivate)
                       .Where(m => m.Parameters.Count == 2)
                       .Select(m => Regex.Match(m.Name, @"(?<=^Migrate_)(\d+)$"))
                       .Where(m => m.Success)
                       .Select(m => Int32.Parse(m.Value));
        }

    }
}