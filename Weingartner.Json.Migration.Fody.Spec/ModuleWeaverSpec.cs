﻿using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows;
using FluentAssertions;
using Mono.Cecil;
using Weingartner.Json.Migration.Common;
using Xunit;
using FieldAttributes = System.Reflection.FieldAttributes;

namespace Weingartner.Json.Migration.Fody.Spec
{
    public class ModuleWeaverSpec : IDisposable
    {
        private readonly ConcurrentBag<string> _AssemblyPaths = new ConcurrentBag<string>();
            
        [Fact]
        public void ShouldInjectVersionProperty()
        {
            Assembly assembly;
            Weave(out assembly);
            var type = assembly.GetType("Weingartner.Json.Migration.TestApplication.TestData");
            var instance = Activator.CreateInstance(type);

            var property = instance.GetType().GetProperty(VersionMemberName.Instance.VersionPropertyName, BindingFlags.Instance | BindingFlags.Public);
            property.Should().NotBeNull();
        }

        [Fact]
        public void ShouldInjectCorrectVersionInTypeThatHasMigrationMethods()
        {
            Assembly assembly;
            Weave(out assembly);
            var type = assembly.GetType("Weingartner.Json.Migration.TestApplication.TestData");
            var instance = Activator.CreateInstance(type);

            var property = instance.GetType().GetProperty(VersionMemberName.Instance.VersionPropertyName, BindingFlags.Instance | BindingFlags.Public);
            property.GetValue(instance).Should().Be(1);
        }

        [Fact]
        public void ShouldInjectCorrectVersionInTypeThatHasNoMigrationMethods()
        {
            Assembly assembly;
            Weave(out assembly);
            var type = assembly.GetType("Weingartner.Json.Migration.TestApplication.TestDataWithoutMigration");
            var instance = Activator.CreateInstance(type);

            var property = instance.GetType().GetProperty(VersionMemberName.Instance.VersionPropertyName, BindingFlags.Instance | BindingFlags.Public);
            property.GetValue(instance).Should().Be(0);
        }

        [Fact]
        public void ShouldInjectDataMemberAttributeIfTypeHasDataContractAttribute()
        {
            Assembly assembly;
            Weave(out assembly);
            var type = assembly.GetType("Weingartner.Json.Migration.TestApplication.TestDataContract");

            type.GetProperty(VersionMemberName.Instance.VersionPropertyName)
                .CustomAttributes
                .Select(attr => attr.AttributeType)
                .Should()
                .Contain(t => t == typeof(DataMemberAttribute));
        }

        [Fact]
        public void ShouldHaveVersion0WhenNoMigrationMethodExists()
        {
            Assembly assembly;
            Weave(out assembly);
            var type = assembly.GetType("Weingartner.Json.Migration.TestApplication.TestDataWithoutMigration");

            // ReSharper disable once PossibleNullReferenceException
            ((int)type.GetField(VersionMemberName.Instance.VersionBackingFieldName, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Should().Be(0);
        }

        [Fact]
        public void ShouldCreateConstVersionField()
        {
            Assembly assembly;
            Weave(out assembly);
            var type = assembly.GetType("Weingartner.Json.Migration.TestApplication.TestData");
            var instance = Activator.CreateInstance(type);

            var field = instance.GetType().GetField(VersionMemberName.Instance.VersionBackingFieldName, BindingFlags.Static | BindingFlags.NonPublic);
            const FieldAttributes attributes = (FieldAttributes.Literal | FieldAttributes.Static);
            // ReSharper disable once PossibleNullReferenceException
            (field.Attributes & attributes).Should().Be(attributes);
        }

        [Fact]
        public void ShouldThrowWhenWeavingInvalidAssembly()
        {
            new Action(WeaveInvalidAssembly).ShouldThrow<MigrationException>();
        }

        [Fact]
        public void ShouldHaveMigrationMethodSignatureInClipboardWhenMigrationMethodMightBeNeeded()
        {
            try
            {
                Clipboard.Clear();
                WeaveInvalidAssembly();
            }
            catch (MigrationException) { }
            finally
            {
                Clipboard.GetText().Should().NotBeEmpty();
            }
        }

        [Fact]
        public void PeVerify()
        {
            Assembly assembly;
            string newAssemblyPath;
            string assemblyPath;
            WeaveValidAssembly(out assembly, out newAssemblyPath, out assemblyPath);

            Verifier.Verify(assemblyPath, newAssemblyPath);
        }

        private void Weave(out Assembly assembly)
        {
            string _, __;
            WeaveValidAssembly(out assembly, out _, out __);
        }

        private void WeaveValidAssembly(out Assembly assembly, out string newAssemblyPath, out string assemblyPath)
        {
            assemblyPath = GetAssemblyPath("Weingartner.Json.Migration.TestApplication");
            Weave(assemblyPath, out assembly, out newAssemblyPath);
        }

        private void WeaveInvalidAssembly()
        {
            var assemblyPath = GetAssemblyPath("Weingartner.Json.Migration.InvalidTestApplication");
            Assembly _;
            string __;
            Weave(assemblyPath, out _, out __); 
        }

        private static string GetAssemblyPath(string assemblyName)
        {
            var path = Path.GetFullPath(string.Format(@"..\..\..\{0}\bin\Debug\{0}.dll", assemblyName));
#if !DEBUG
            path = path.Replace(@"\Debug\", @"\Release\");
#endif
            return path;
        }

        private void Weave(string assemblyPath, out Assembly assembly, out string newAssemblyPath)
        {
            newAssemblyPath = Path.ChangeExtension(assemblyPath, Guid.NewGuid() + ".dll");
            File.Copy(assemblyPath, newAssemblyPath, true);

            _AssemblyPaths.Add(newAssemblyPath);

            var moduleDefinition = ModuleDefinition.ReadModule(newAssemblyPath);
            var weavingTask = new ModuleWeaver
            {
                ModuleDefinition = moduleDefinition
            };

            weavingTask.Execute();
            moduleDefinition.Write(newAssemblyPath);

            assembly = Assembly.LoadFile(newAssemblyPath);
        }

        public void Dispose()
        {
            foreach (var path in _AssemblyPaths)
            {
                try
                {
                    File.Delete(path);
                }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
