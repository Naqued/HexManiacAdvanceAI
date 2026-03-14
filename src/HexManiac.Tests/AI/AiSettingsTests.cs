using HavenSoft.HexManiac.Core.Models;
using HavenSoft.HexManiac.Core.ViewModels.AI;
using System.Collections.Generic;
using Xunit;

namespace HavenSoft.HexManiac.Tests.AI {
   public class AiSettingsTests {
      private static StubFileSystem CreateStubFileSystem(Dictionary<string, string[]> metadata = null) {
         metadata ??= new Dictionary<string, string[]>();
         var fs = new StubFileSystem {
            MetadataFor = key => metadata.TryGetValue(key, out var m) ? m : null,
            SaveMetadata = (key, data) => { metadata[key] = data; return true; },
         };
         return fs;
      }

      [Fact]
      public void Defaults_WhenNoMetadata() {
         var fs = CreateStubFileSystem();
         var settings = new AiSettings(fs);

         Assert.Equal(string.Empty, settings.ApiKey);
         Assert.Equal("claude-sonnet-4-20250514", settings.ModelName);
         Assert.Equal("Anthropic", settings.ProviderName);
         Assert.Equal("https://api.anthropic.com", settings.BaseUrl);
         Assert.Equal(80000, settings.MaxContextTokens);
      }

      [Fact]
      public void IsConfigured_FalseWhenNoApiKey() {
         var fs = CreateStubFileSystem();
         var settings = new AiSettings(fs);

         Assert.False(settings.IsConfigured);
      }

      [Fact]
      public void IsConfigured_TrueWhenApiKeySet() {
         var fs = CreateStubFileSystem();
         var settings = new AiSettings(fs);
         settings.ApiKey = "sk-test-key";

         Assert.True(settings.IsConfigured);
      }

      [Fact]
      public void SaveLoad_RoundTrip() {
         var metadata = new Dictionary<string, string[]>();
         var fs = CreateStubFileSystem(metadata);

         var settings1 = new AiSettings(fs);
         settings1.ApiKey = "test-key-123";
         settings1.ModelName = "claude-opus-4-20250514";
         settings1.BaseUrl = "https://custom.api.com";
         settings1.MaxContextTokens = 50000;

         // Create new settings instance that loads from same metadata
         var settings2 = new AiSettings(fs);

         Assert.Equal("test-key-123", settings2.ApiKey);
         Assert.Equal("claude-opus-4-20250514", settings2.ModelName);
         Assert.Equal("https://custom.api.com", settings2.BaseUrl);
         Assert.Equal(50000, settings2.MaxContextTokens);
      }

      [Fact]
      public void Save_PersistsToMetadata() {
         var metadata = new Dictionary<string, string[]>();
         var fs = CreateStubFileSystem(metadata);

         var settings = new AiSettings(fs);
         settings.ApiKey = "my-key";

         Assert.True(metadata.ContainsKey("AiSettings"));
         var lines = metadata["AiSettings"];
         Assert.Contains(lines, l => l.Contains("ApiKey = my-key"));
      }

      [Fact]
      public void Load_ParsesExistingMetadata() {
         var metadata = new Dictionary<string, string[]> {
            ["AiSettings"] = new[] {
               "[AiSettings]",
               "ApiKey = saved-key",
               "ModelName = test-model",
               "ProviderName = TestProvider",
               "BaseUrl = https://test.api.com",
               "MaxContextTokens = 60000",
            }
         };
         var fs = CreateStubFileSystem(metadata);
         var settings = new AiSettings(fs);

         Assert.Equal("saved-key", settings.ApiKey);
         Assert.Equal("test-model", settings.ModelName);
         Assert.Equal("TestProvider", settings.ProviderName);
         Assert.Equal("https://test.api.com", settings.BaseUrl);
         Assert.Equal(60000, settings.MaxContextTokens);
      }

      [Fact]
      public void PropertyChanged_RaisedOnApiKeyChange() {
         var fs = CreateStubFileSystem();
         var settings = new AiSettings(fs);
         var changed = false;
         settings.PropertyChanged += (s, e) => {
            if (e.PropertyName == nameof(AiSettings.ApiKey)) changed = true;
         };

         settings.ApiKey = "new-key";
         Assert.True(changed);
      }

      [Fact]
      public void Load_IgnoresMalformedLines() {
         var metadata = new Dictionary<string, string[]> {
            ["AiSettings"] = new[] {
               "[AiSettings]",
               "badline",
               "ApiKey = good-key",
               "also bad",
            }
         };
         var fs = CreateStubFileSystem(metadata);
         var settings = new AiSettings(fs);

         Assert.Equal("good-key", settings.ApiKey);
      }
   }
}
