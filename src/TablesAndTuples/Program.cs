﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using SimpleJson;

namespace TablesAndTuples
{
    class Program
    {
        static readonly XNamespace ns = "http://wixtoolset.org/schemas/v4/wi/tables";
        static readonly XName TableDefinition = ns + "tableDefinition";
        static readonly XName ColumnDefinition = ns + "columnDefinition";
        static readonly XName Name = "name";
        static readonly XName Type = "type";

        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                return;
            }
            else if (Path.GetExtension(args[0]) == ".xml")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Need to specify output json file as well.");
                }

                ReadXmlWriteJson(Path.GetFullPath(args[0]), Path.GetFullPath(args[1]));
            }
            else if (Path.GetExtension(args[0]) == ".json")
            {
                string prefix = null;
                if (args.Length < 2)
                {
                    Console.WriteLine("Need to specify output folder.");
                }
                else if (args.Length > 2)
                {
                    prefix = args[2];
                }

                ReadJsonWriteCs(Path.GetFullPath(args[0]), Path.GetFullPath(args[1]), prefix);
            }
        }

        private static void ReadXmlWriteJson(string inputPath, string outputPath)
        {
            var doc = XDocument.Load(inputPath);

            var array = new JsonArray();

            foreach (var tableDefinition in doc.Descendants(TableDefinition))
            {
                var tupleType = tableDefinition.Attribute(Name).Value;

                var fields = new JsonArray();

                foreach (var columnDefinition in tableDefinition.Elements(ColumnDefinition))
                {
                    var fieldName = columnDefinition.Attribute(Name).Value;
                    var type = columnDefinition.Attribute(Type).Value;

                    if (type == "localized")
                    {
                        type = "string";
                    }
                    else if (type == "object")
                    {
                        type = "path";
                    }

                    var field = new JsonObject
                    {
                        { fieldName, type }
                    };

                    fields.Add(field);
                }

                var obj = new JsonObject
                {
                    { tupleType, fields }
                };
                array.Add(obj);
            }

            array.Sort(CompareTupleDefinitions);

            var strat = new PocoJsonSerializerStrategy();
            var json = SimpleJson.SimpleJson.SerializeObject(array, strat);

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.WriteAllText(outputPath, json);
        }

        private static void ReadJsonWriteCs(string inputPath, string outputFolder, string prefix)
        {
            var json = File.ReadAllText(inputPath);
            var tuples = SimpleJson.SimpleJson.DeserializeObject(json) as JsonArray;

            var tupleNames = new List<string>();

            foreach (var tupleDefinition in tuples.Cast<JsonObject>())
            {
                var tupleName = tupleDefinition.Keys.Single();
                var fields = tupleDefinition.Values.Single() as JsonArray;

                var list = GetFields(fields).ToList();

                tupleNames.Add(tupleName);

                var text = GenerateTupleFileText(prefix, tupleName, list);

                var pathTuple = Path.Combine(outputFolder, tupleName + "Tuple.cs");
                Console.WriteLine("Writing: {0}", pathTuple);
                File.WriteAllText(pathTuple, text);
            }

            var content = TupleNamesFileContent(prefix, tupleNames);
            var pathNames = Path.Combine(outputFolder, String.Concat(prefix, "TupleDefinitions.cs"));
            Console.WriteLine("Writing: {0}", pathNames);
            File.WriteAllText(pathNames, content);
        }

        private static IEnumerable<(string Name, string Type, string ClrType, string AsFunction)> GetFields(JsonArray fields)
        {
            foreach (var field in fields.Cast<JsonObject>())
            {
                var fieldName = field.Keys.Single();
                var fieldType = field.Values.Single() as string;

                var clrType = ConvertToClrType(fieldType);
                fieldType = ConvertToFieldType(fieldType);

                var asFunction = $"As{fieldType}()";

                yield return (Name: fieldName, Type: fieldType, ClrType: clrType, AsFunction: asFunction);
            }
        }

        private static string GenerateTupleFileText(string prefix, string tupleName, List<(string Name, string Type, string ClrType, string AsFunction)> tupleFields)
        {
            var ns = prefix ?? "Data";
            var toString = String.IsNullOrEmpty(prefix) ? null : ".ToString()";

            var startTupleDef = String.Join(Environment.NewLine,
                "// Copyright (c) .NET Foundation and contributors. All rights reserved. Licensed under the Microsoft Reciprocal License. See LICENSE.TXT file in the project root for full license information.",
                "",
                "namespace WixToolset.{2}",
                "{",
                "    using WixToolset.Data;",
                "    using WixToolset.{2}.Tuples;",
                "",
                "    public static partial class {3}TupleDefinitions",
                "    {",
                "        public static readonly IntermediateTupleDefinition {1} = new IntermediateTupleDefinition(",
                "            {3}TupleDefinitionType.{1}{4},",
                "            new[]",
                "            {");
            var fieldDef =
                "                new IntermediateFieldDefinition(nameof({1}TupleFields.{2}), IntermediateFieldType.{3}),";
            var endTupleDef = String.Join(Environment.NewLine,
                "            },",
                "            typeof({1}Tuple));",
                "    }",
                "}",
                "",
                "namespace WixToolset.{2}.Tuples",
                "{",
                "    using WixToolset.Data;",
                "",
                "    public enum {1}TupleFields",
                "    {");
            var fieldEnum =
                "        {2},";
            var startTuple = String.Join(Environment.NewLine,
                "    }",
                "",
                "    public class {1}Tuple : IntermediateTuple",
                "    {",
                "        public {1}Tuple() : base({3}TupleDefinitions.{1}, null, null)",
                "        {",
                "        }",
                "",
                "        public {1}Tuple(SourceLineNumber sourceLineNumber, Identifier id = null) : base({3}TupleDefinitions.{1}, sourceLineNumber, id)",
                "        {",
                "        }",
                "",
                "        public IntermediateField this[{1}TupleFields index] => this.Fields[(int)index];");
            var fieldProp = String.Join(Environment.NewLine,
                "",
                "        public {4} {2}",
                "        {",
                "            get => this.Fields[(int){1}TupleFields.{2}].{5};",
                "            set => this.Set((int){1}TupleFields.{2}, value);",
                "        }");
            var endTuple = String.Join(Environment.NewLine,
                "    }",
                "}");

            var sb = new StringBuilder();

            sb.AppendLine(startTupleDef.Replace("{1}", tupleName).Replace("{2}", ns).Replace("{3}", prefix).Replace("{4}", toString));
            foreach (var field in tupleFields)
            {
                sb.AppendLine(fieldDef.Replace("{1}", tupleName).Replace("{2}", field.Name).Replace("{3}", field.Type).Replace("{4}", field.ClrType).Replace("{5}", field.AsFunction));
            }
            sb.AppendLine(endTupleDef.Replace("{1}", tupleName).Replace("{2}", ns).Replace("{3}", prefix));
            foreach (var field in tupleFields)
            {
                sb.AppendLine(fieldEnum.Replace("{1}", tupleName).Replace("{2}", field.Name).Replace("{3}", field.Type).Replace("{4}", field.ClrType).Replace("{5}", field.AsFunction));
            }
            sb.AppendLine(startTuple.Replace("{1}", tupleName).Replace("{2}", ns).Replace("{3}", prefix));
            foreach (var field in tupleFields)
            {
                sb.AppendLine(fieldProp.Replace("{1}", tupleName).Replace("{2}", field.Name).Replace("{3}", field.Type).Replace("{4}", field.ClrType).Replace("{5}", field.AsFunction));
            }
            sb.Append(endTuple);

            return sb.ToString();
        }

        private static string TupleNamesFileContent(string prefix, List<string> tupleNames)
        {
            var ns = prefix ?? "Data";

            var header = String.Join(Environment.NewLine,
                "// Copyright (c) .NET Foundation and contributors. All rights reserved. Licensed under the Microsoft Reciprocal License. See LICENSE.TXT file in the project root for full license information.",
                "",
                "namespace WixToolset.{2}",
                "{",
                "    using System;",
                "    using WixToolset.Data;",
                "",
                "    public enum {3}TupleDefinitionType",
                "    {");
            var namesFormat =
                "        {1},";
            var midpoint = String.Join(Environment.NewLine,
                "    }",
                "",
                "    public static partial class {3}TupleDefinitions",
                "    {",
                "        public static readonly Version Version = new Version(\"4.0.0\");",
                "",
                "        public static IntermediateTupleDefinition ByName(string name)",
                "        {",
                "            if (!Enum.TryParse(name, out {3}TupleDefinitionType type))",
                "            {",
                "                return null;",
                "            }",
                "",
                "            return ByType(type);",
                "        }",
                "",
                "        public static IntermediateTupleDefinition ByType({3}TupleDefinitionType type)",
                "        {",
                "            switch (type)",
                "            {");

            var caseFormat = String.Join(Environment.NewLine,
                "                case {3}TupleDefinitionType.{1}:",
                "                    return {3}TupleDefinitions.{1};",
                "");

            var footer = String.Join(Environment.NewLine,
                "                default:",
                "                    throw new ArgumentOutOfRangeException(nameof(type));",
                "            }",
                "        }",
                "    }",
                "}");

            var sb = new StringBuilder();

            sb.AppendLine(header.Replace("{2}", ns).Replace("{3}", prefix));
            foreach (var tupleName in tupleNames)
            {
                sb.AppendLine(namesFormat.Replace("{1}", tupleName).Replace("{2}", ns).Replace("{3}", prefix));
            }
            sb.AppendLine(midpoint.Replace("{2}", ns).Replace("{3}", prefix));
            foreach (var tupleName in tupleNames)
            {
                sb.AppendLine(caseFormat.Replace("{1}", tupleName).Replace("{2}", ns).Replace("{3}", prefix));
            }
            sb.AppendLine(footer);

            return sb.ToString();
        }

        private static string ConvertToFieldType(string fieldType)
        {
            switch (fieldType.ToLowerInvariant())
            {
                case "bool":
                    return "Bool";

                case "string":
                case "preserved":
                    return "String";

                case "number":
                    return "Number";

                case "path":
                    return "Path";
            }

            throw new ArgumentException(fieldType);
        }

        private static string ConvertToClrType(string fieldType)
        {
            switch (fieldType.ToLowerInvariant())
            {
                case "bool":
                    return "bool";

                case "string":
                case "preserved":
                    return "string";

                case "number":
                    return "int";

                case "path":
                    return "string";
            }

            throw new ArgumentException(fieldType);
        }

        private static int CompareTupleDefinitions(object x, object y)
        {
            var first = (JsonObject)x;
            var second = (JsonObject)y;

            var firstType = first.Keys.Single();
            var secondType = second.Keys.Single();

            return firstType.CompareTo(secondType);
        }
    }
}
