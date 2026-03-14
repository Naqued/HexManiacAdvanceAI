using HavenSoft.HexManiac.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HavenSoft.HexManiac.Core.ViewModels.AI {
   public class AiSettings : ViewModelCore {
      private readonly IFileSystem fileSystem;
      private const string SettingsFileName = "AiSettings";

      private string apiKey = string.Empty;
      public string ApiKey {
         get => apiKey;
         set { Set(ref apiKey, value ?? string.Empty); Save(); }
      }

      private string modelName = "claude-sonnet-4-20250514";
      public string ModelName {
         get => modelName;
         set { Set(ref modelName, value ?? "claude-sonnet-4-20250514"); Save(); }
      }

      private string providerName = "Anthropic";
      public string ProviderName {
         get => providerName;
         set { Set(ref providerName, value ?? "Anthropic"); Save(); }
      }

      private string baseUrl = "https://api.anthropic.com";
      public string BaseUrl {
         get => baseUrl;
         set { Set(ref baseUrl, value ?? "https://api.anthropic.com"); Save(); }
      }

      private int maxContextTokens = 80000;
      public int MaxContextTokens {
         get => maxContextTokens;
         set { Set(ref maxContextTokens, value); Save(); }
      }

      public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

      public AiSettings(IFileSystem fileSystem) {
         this.fileSystem = fileSystem;
         Load();
      }

      private void Load() {
         var metadata = fileSystem.MetadataFor(SettingsFileName);
         if (metadata == null) return;
         foreach (var line in metadata) {
            var parts = line.Split(new[] { " = " }, 2, StringSplitOptions.None);
            if (parts.Length != 2) continue;
            switch (parts[0].Trim()) {
               case "ApiKey": apiKey = parts[1].Trim(); break;
               case "ModelName": modelName = parts[1].Trim(); break;
               case "ProviderName": providerName = parts[1].Trim(); break;
               case "BaseUrl": baseUrl = parts[1].Trim(); break;
               case "MaxContextTokens": int.TryParse(parts[1].Trim(), out maxContextTokens); break;
            }
         }
      }

      private void Save() {
         var lines = new List<string> {
            "[AiSettings]",
            $"ApiKey = {ApiKey}",
            $"ModelName = {ModelName}",
            $"ProviderName = {ProviderName}",
            $"BaseUrl = {BaseUrl}",
            $"MaxContextTokens = {MaxContextTokens}",
         };
         fileSystem.SaveMetadata(SettingsFileName, lines.ToArray());
      }
   }
}
