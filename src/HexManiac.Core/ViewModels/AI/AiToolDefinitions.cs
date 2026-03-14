using System.Collections.Generic;

namespace HavenSoft.HexManiac.Core.ViewModels.AI {
   public static class AiToolDefinitions {
      public static List<ToolDefinition> GetToolDefinitions() {
         return new List<ToolDefinition> {
            new ToolDefinition {
               Name = "navigate_to",
               Description = "Navigate the editor to a specific address or anchor name. Examples: '0x1A0000', 'data.pokemon.stats'",
               InputSchemaJson = @"{""type"":""object"",""properties"":{""target"":{""type"":""string"",""description"":""Address (hex like 0x1A0000) or anchor name to navigate to""}},""required"":[""target""]}"
            },
            new ToolDefinition {
               Name = "decompile_script",
               Description = "Decompile a script at the given address. Returns the human-readable script text.",
               InputSchemaJson = @"{""type"":""object"",""properties"":{""address"":{""type"":""string"",""description"":""Hex address of the script (e.g. 0x1A0000)""},""script_type"":{""type"":""string"",""enum"":[""xse"",""bse"",""ase"",""tse""],""description"":""Script type: xse (event), bse (battle), ase (animation), tse (trainer AI)""}},""required"":[""address""]}"
            },
            new ToolDefinition {
               Name = "compile_script",
               Description = "Compile a script and write it to the ROM. This is a WRITE operation that modifies data. The script will be compiled and placed in free space.",
               InputSchemaJson = @"{""type"":""object"",""properties"":{""script_text"":{""type"":""string"",""description"":""The script text to compile""},""script_type"":{""type"":""string"",""enum"":[""xse"",""bse"",""ase"",""tse""],""description"":""Script type""},""address"":{""type"":""string"",""description"":""Optional: address to compile at. If omitted, finds free space.""}},""required"":[""script_text""]}"
            },
            new ToolDefinition {
               Name = "read_table",
               Description = "Read data from a table by anchor name and optional index. Returns the field values.",
               InputSchemaJson = @"{""type"":""object"",""properties"":{""anchor"":{""type"":""string"",""description"":""Table anchor name (e.g. data.pokemon.stats)""},""index"":{""type"":""integer"",""description"":""Optional element index (0-based)""}},""required"":[""anchor""]}"
            },
            new ToolDefinition {
               Name = "write_table_field",
               Description = "Write a value to a specific table field. This is a WRITE operation that modifies data.",
               InputSchemaJson = @"{""type"":""object"",""properties"":{""anchor"":{""type"":""string"",""description"":""Table anchor name""},""index"":{""type"":""integer"",""description"":""Element index (0-based)""},""field"":{""type"":""string"",""description"":""Field name""},""value"":{""type"":""string"",""description"":""Value to write""}},""required"":[""anchor"",""index"",""field"",""value""]}"
            },
            new ToolDefinition {
               Name = "find_free_space",
               Description = "Find a block of free space (0xFF bytes) in the ROM of the given length.",
               InputSchemaJson = @"{""type"":""object"",""properties"":{""length"":{""type"":""integer"",""description"":""Number of free bytes needed""}},""required"":[""length""]}"
            },
            new ToolDefinition {
               Name = "search_anchors",
               Description = "Search for anchors by partial name. Returns matching anchor names and addresses.",
               InputSchemaJson = @"{""type"":""object"",""properties"":{""partial_name"":{""type"":""string"",""description"":""Partial anchor name to search for""}},""required"":[""partial_name""]}"
            },
            new ToolDefinition {
               Name = "read_bytes",
               Description = "Read raw bytes from a ROM address. Returns hex string.",
               InputSchemaJson = @"{""type"":""object"",""properties"":{""address"":{""type"":""string"",""description"":""Hex address to read from (e.g. 0x1A0000)""},""length"":{""type"":""integer"",""description"":""Number of bytes to read (max 256)""}},""required"":[""address"",""length""]}"
            },
         };
      }
   }
}
