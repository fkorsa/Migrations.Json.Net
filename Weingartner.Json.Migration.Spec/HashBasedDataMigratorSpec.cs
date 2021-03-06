﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Weingartner.Json.Migration.Common;
using Xunit;

namespace Weingartner.Json.Migration.Spec
{
    public class HashBasedDataMigratorSpec
    {
        public HashBasedDataMigratorSpec()
        {
            Serializer = new JsonSerializer();
        }

        public static JsonSerializer Serializer = new JsonSerializer();

        [Theory]
        [InlineData(0, "Name_0_1_2")]
        [InlineData(1, "Name_1_2")]
        [InlineData(2, "Name_2")]
        public void ShouldApplyChangesMadeByTheMigrationMethods(int configVersion, string expectedName)
        {
            var configData = CreateConfigurationData(configVersion);

            var sut = CreateMigrator();
            var result = sut.TryMigrate(configData, typeof(FixtureData), Serializer);

            result.Item2.Should().BeTrue();
            result.Item1["Name"].Value<string>().Should().Be(expectedName);
        }

        [Fact]
        public void ShouldBeAbleToReplaceWholeObject()
        {
            var configData = JToken.FromObject(new[] { 1, 2, 3 });

            var sut = CreateMigrator();
            var result = sut.TryMigrate(configData, typeof(FixtureData2), Serializer);

            result.Item1.Should().BeOfType<JObject>();
        }

        [Fact]
        public void ShouldHaveCorrectVersionAfterMigration()
        {
            var configData = CreateConfigurationData(0);

            var sut = CreateMigrator();
            var result = sut.TryMigrate(configData, typeof(FixtureData), Serializer);

            result.Item2.Should().BeTrue();
            result.Item1[VersionMemberName.VersionPropertyName].Value<int>().Should().Be(3);
        }

        [Fact]
        public void ShouldWorkWithNonVersionedData()
        {
            var configData = CreateConfigurationData(0);
            ((JObject)configData).Remove(VersionMemberName.VersionPropertyName);

            var sut = CreateMigrator();
            new Action(() => sut.TryMigrate(configData, typeof(FixtureData), Serializer)).Should().NotThrow();
        }

        [Fact]
        public void ShouldThrowIfConfigurationDataIsNull()
        {
            var sut = CreateMigrator();
            new Action(() => sut.TryMigrate(null, typeof(FixtureData), Serializer)).Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void ShouldThrowIfConfigurationDataTypeIsNull()
        {
            var configData = CreateConfigurationData(0);

            var sut = CreateMigrator();
            new Action(() => sut.TryMigrate(configData, null, Serializer)).Should().Throw<ArgumentNullException>();
        }

        [Theory]
        [InlineData(typeof(DataWithInvalidMigrationMethod))]
        [InlineData(typeof(DataWithInvalidMigrationMethod2))]
        public void ShouldThrowIfMigrationMethodIsInvalid(Type configType)
        {
            var configData = new JObject {[VersionMemberName.VersionPropertyName] = 0};

            var sut = CreateMigrator();
            new Action(() => sut.TryMigrate(configData, configType, Serializer)).Should().Throw<MigrationException>();
        }


        [Fact]
        public void ShouldNotChangeDataWhenTypeIsNotMigratable()
        {
            var configData = JToken.FromObject(new NotMigratableData("Test"));
            var origConfigData = configData.DeepClone();

            var sut = CreateMigrator();
            var result = sut.TryMigrate(configData, typeof(NotMigratableData), Serializer);
            result.Item2.Should().BeFalse();
            result.Item1.Should().Match((JToken p) => JToken.DeepEquals(p, origConfigData));
        }

        [Fact]
        public void ShouldThrowWhenVersionOfDataIsTooHigh()
        {
            var configData = CreateConfigurationFromObject(new FixtureData(), FixtureData._version + 1);

            var sut = CreateMigrator();
            new Action(() => sut.TryMigrate(configData, typeof(FixtureData), Serializer)).Should().Throw<DataVersionTooHighException>();
        }

        [Fact]
        public void ShouldBeAbleToMigrateAnArray()
        {
            var ints = new[] { 3, 4, 5 }.ToList();
            var configData = JToken.FromObject( ints);

            var sut = CreateMigrator();
            var result = sut.TryMigrate(configData, typeof (ArrayFixtureData), Serializer);
            result.Item2.Should().BeTrue();
            result.Item1["Name"].ToString().Should().Be("Brad");
            result.Item1["Data"].ToList().Should().BeEquivalentTo(ints.Select(i=>JToken.FromObject(i)));
        }

        // ReSharper disable UnusedMember.Local
        [Migratable("")]
        private class ArrayFixtureData
        {
            [DataMember]
            public string Name { get; private set; }

            [DataMember]
            public List<int> Data { get; private set; } 

            // ReSharper disable once UnusedParameter.Local
            private static JObject Migrate_1(JToken data, JsonSerializer serializer)
            {
                var array = data as JArray;

                var obj = new JObject
                {
                    ["Data"] = array,
                    ["Name"] = "Brad"
                };

                return obj;

            }
        }
        // ReSharper restore UnusedMember.Local

        private static IMigrateData<JToken> CreateMigrator()
        {
            return new HashBasedDataMigrator<JToken>(new JsonVersionUpdater());
        }

        private static JToken CreateConfigurationData(int version)
        {
            return CreateConfigurationFromObject(new FixtureData(), version);
        }

        private static JToken CreateConfigurationFromObject(object obj, int version)
        {
            var data = JToken.FromObject(obj);
            data[VersionMemberName.VersionPropertyName] = version;
            return data;
        }

        // ReSharper disable UnusedMember.Local
        // ReSharper disable UnusedParameter.Local
        // ReSharper disable UnusedField.Compiler
        // ReSharper disable InconsistentNaming


        [Migratable("")]
        private class FixtureData
        {
            public const int _version = 3;

            [DataMember]
            public string Name { get; private set; }

            public FixtureData()
            {
                Name = "Name";
            }

            private static JObject Migrate_1(JObject data, JsonSerializer serializer)
            {
                data["Name"] += "_0";
                return data;
            }

            private static JObject Migrate_2(JObject data, JsonSerializer serializer)
            {
                data["Name"] += "_1";
                return data;
            }

            private static JObject Migrate_3(JObject data, JsonSerializer serializer)
            {
                data["Name"] += "_2";
                return data;
            }
        }

        [Migratable("")]
        private class FixtureData2
        {
            public const int _version = 1;

            [DataMember]
            public int[] Values { get; private set; }

            private static JToken Migrate_1(JToken data, JsonSerializer serializer)
            {
                data = new JObject { { "Values", data } };
                return data;
            }
        }

        [Migratable("")]
        private class DataWithInvalidMigrationMethod
        {
            public const int _version = 3;

            [DataMember]
            public string Name { get; private set; }

            private static JToken Migrate_1(JToken data, string additionalData) { return data; }
        }

        [Migratable("")]
        private class DataWithInvalidMigrationMethod2
        {
            public const int _version = 3;

            [DataMember]
            public string Name { get; private set; }

            private static object Migrate_1(JToken data) { return data; }
        }

        [Migratable("")]
        private class DataWithoutVersion
        {
            [DataMember]
            public string Name { get; private set; }

            private static JToken Migrate_1(JToken data, JsonSerializer serializer)
            {
                return data;
            }
        }

        private class NotMigratableData
        {
            [DataMember]
            public string Name { get; private set; }

            public NotMigratableData(string name)
            {
                Name = name;
            }

            private static JToken Migrate_1(JToken data, JsonSerializer serializer)
            {
                data["Name"] += " - migrated";
                return data;
            }
        }

        [Migratable("")]
        public class FixtureDataWithCustomMigrator
        {
            public const int _version = 3;

            [DataMember]
            public string Name { get; private set; }
        }

        public class FixtureDataMigrator
        {
            private static JObject Migrate_1(JObject data, JsonSerializer serializer)
            {
                data["Name"] = "Name_A";
                return data;
            }

            private static JObject Migrate_2(JObject data, JsonSerializer serializer)
            {
                data["Name"] += "_B";
                return data;
            }

            private static JObject Migrate_3(JObject data, JsonSerializer serializer)
            {
                data["Name"] += "_C";
                return data;
            }
        }

        // ReSharper restore InconsistentNaming
        // ReSharper restore UnusedField.Compiler
        // ReSharper restore UnusedParameter.Local
        // ReSharper restore UnusedMember.Local
    }
}
