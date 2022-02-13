using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Dapper;
using Npgsql;
using System.IO;
using Newtonsoft.Json;

namespace FluentMigration.Generator.PostgreSql
{
    public static class GenerateMigrationHelper
    {
        public static void Process(string ConnectionString, Assembly Assembly, bool ConvertCamelToUnderscore = false)
        {
            try
            {

                var nSpace = "MigrationGenerator.PostgreSql.Migrations";
                using var dbConnection = new NpgsqlConnection(ConnectionString);
                dbConnection.Open();
                var type = Assembly.GetTypes()
                .Where(Type =>
                       Type.IsClass && Type.GetCustomAttributes(true)
                   .Any(Att => Att.GetType() == typeof(TableAttribute)));
                var changeCount = 0;
                var version = GenerateMigrationVersion();
                var fileName = GenerateMigrationFileName(version);
                var migCode = GetMigrationInitialCode(nSpace, version.ToString(), fileName);
                var upMethodCode = "";
                var downMethodCode = "";
                var counter = 1;
                foreach (var t in type)
                {
                    var n = t.Name;
                    var attrributes = t.GetCustomAttributes(true);
                    var props = t.GetProperties();
                    var tableDef = GetTableDefinition(t);
                    var tableDefDb = GetTableDefinitionFromDb(dbConnection, tableDef.Schema, tableDef.TableName);

                    if (!tableDefDb.Any())
                    {
                        // creation migration of  new table ;
                        upMethodCode += GetCreateTableMigrationLine(tableDef.TableName, tableDef.Schema) + "\n";
                        var sb = new StringBuilder();
                        foreach (var prop in props)
                        {
                            sb.AppendLine(GetColumnCreationLine(prop));
                        }
                        upMethodCode += sb.ToString() + ";";
                        downMethodCode += GetDeleteTableMigrationLine(tableDef.TableName, tableDef.Schema) + "\n";
                        changeCount += 1;
                    }
                    else
                    {
                        foreach (var prop in props)
                        {
                            var columnName = GetColumnColumnNameFromAttribute(prop);
                            if (!tableDefDb.Any(X => X.ColumnName.ToLower() == columnName))
                            {
                                // Add new column
                                upMethodCode += GetAlterTableMigrationLine(tableDef.TableName, tableDef.Schema);
                                upMethodCode += GetColumnCreationLine(prop, false) + ";";
                                downMethodCode += GetDeleteColumnMigrationLine(prop.Name.ToLower(), tableDef.TableName, tableDef.Schema) + "\n";
                                changeCount += 1;
                            }
                        }
                    }
                    if (changeCount == 2)
                    {
                        migCode += GetMigrationUpMethodCode();
                        migCode += upMethodCode;
                        migCode += "\n}";
                        migCode += GetMigrationDownMethodCode();
                        migCode += downMethodCode;
                        migCode += "\n}";
                        migCode += "\n}\n}";
                        System.IO.File.WriteAllText(fileName + ".cs", migCode);
                        version = GenerateMigrationVersion();
                        fileName = GenerateMigrationFileName(version);
                        migCode = GetMigrationInitialCode(nSpace, version.ToString(), fileName);
                        upMethodCode = "";
                        downMethodCode = "";
                        changeCount = 0;
                    }
                    else if (counter == type.Count() && changeCount > 0)
                    {
                        migCode += GetMigrationUpMethodCode();
                        migCode += upMethodCode;
                        migCode += "\n}";
                        migCode += GetMigrationDownMethodCode();
                        migCode += downMethodCode;
                        migCode += "\n}";
                        migCode += "\n}\n}";
                        System.IO.File.WriteAllText(fileName + ".cs", migCode);
                    }
                    counter += 1;
                }
                dbConnection.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
        private static TableDefinitionBase GetTableDefinition(Type T)
        {
            var result = new TableDefinitionBase
            {
                Schema = "public",
                TableName = ""
            };
            var attributes = T.GetCustomAttributes(true).Where(X => X.GetType() == typeof(TableAttribute)).ToList();
            if (attributes.Any())
            {
                var tableAttribute = (TableAttribute)attributes.First();
                result.Schema = tableAttribute.Schema.ToLower();
                result.TableName = tableAttribute.Name.ToLower();
            }
            else
            {
                result.TableName = T.Name.ToLower();
            }

            return result;
        }
        private static string GetCreateTableMigrationLine(string TableName, string Schema)
        {
            return @$" Create.Table(""{TableName}"").InSchema(""{Schema}"") ";
        }
        private static string GetColumnDefinitionQuery()
        {
            return @"SELECT
                    i.table_schema as schema ,i.table_name tablename ,i.column_name columnname ,i.data_type datatype ,i.is_nullable isnullable,i.is_identity isidentity 
                    FROM
                    information_schema.columns i ";
        }
        private static List<TableDefinition> GetTableDefinitionFromDb(NpgsqlConnection con, string Schema, string TableName)
        {
            var query = GetColumnDefinitionQuery();
            query += @" where i.table_schema=@Schema and i.table_name=@TableName ";
            return con.Query<TableDefinition>(query, new { Schema, TableName }).ToList();
        }
        private static string GenerateMigrationVersion()
        {
            return "" + DateTime.Now.Year + DateTime.Now.Month + DateTime.Now.Day + DateTime.Now.Hour + DateTime.Now.Minute + DateTime.Now.Second + DateTime.Now.Millisecond;
        }
        private static string GenerateMigrationFileName(string Version)
        {
            return "Migration_" + Version;
        }
        private static string GetMigrationInitialCode(string NameSpace, string Version, string ClassName)
        {
            return $@" using FluentMigrator;
                    using System;
                    namespace {NameSpace}
                   {{
                        [Migration({Version})]
                        public class {ClassName} : Migration
                        {{";

        }
        private static string GetMigrationUpMethodCode()
        {
            return @"public override void Up()
        {";
        }
        private static string GetMigrationDownMethodCode()
        {
            return @"public override void Down()
        {";
        }
        private static string GetColumnCreationLine(PropertyInfo info, bool ForCreateTable = true)
        {
            var columnName = info.Name.ToLower();
            var columnDataType = "";
            var dataType = GetColumnDataTypeFromAttribute(info);
            if (!string.IsNullOrEmpty(dataType))
            {
                columnDataType = $@"AsCustom(""{dataType}"")";
            }
            else
            {
                columnDataType = GetColumnDataType(info);
            }
            var columnNameFromAttri = GetColumnColumnNameFromAttribute(info);
            if (!string.IsNullOrEmpty(columnNameFromAttri))
                columnName = columnNameFromAttri;

            var result = $@"{(ForCreateTable ? ".WithColumn" : ".AddColumn")}(""{columnName}"").{columnDataType}";
            if (Nullable.GetUnderlyingType(info.PropertyType) != null)
            {
                result += ".Nullable()";
            }
            else
            {
                result += ".NotNullable()";
            }
            if (columnName.ToLower() == "id")
                result += ".PrimaryKey()";
            return result;

        }
        private static string GetColumnDataType(PropertyInfo info)
        {
            return info.PropertyType switch
            {
                Type intType when (intType == typeof(Int32) || intType == typeof(Int32?)) => "AsInt32()",
                Type intType when (intType == typeof(int) || intType == typeof(int?)) => "AsInt32()",
                Type intType when (intType == typeof(short) || intType == typeof(short?)) => "AsInt16()",
                Type intType when (intType == typeof(long) || intType == typeof(long?)) => "AsInt64()",
                Type doubleType when (doubleType == typeof(double) || doubleType == typeof(double?)) => "AsDouble()",
                Type decimalType when (decimalType == typeof(decimal) || decimalType == typeof(decimal?)) => "AsDecimal()",
                Type boolType when (boolType == typeof(bool) || boolType == typeof(bool?)) => "AsBoolean()",
                Type guidType when (guidType == typeof(Guid) || guidType == typeof(Guid?)) => "AsGuid()",
                Type dateType when (dateType == typeof(DateTime) || dateType == typeof(DateTime?)) => "AsDateTime()",
                Type stringType when stringType == typeof(string) => "AsString(100)",
                _ => "",
            };
        }
        private static string GetColumnColumnNameFromAttribute(PropertyInfo info)
        {
            if (info.GetCustomAttributes(true).Any(X => X.GetType() == typeof(ColumnAttribute)))
            {
                var columnAttrri = info.GetCustomAttribute<ColumnAttribute>();
                if (columnAttrri != null)
                {
                    return columnAttrri.Name;
                }
            }
            return info.Name.ToLower();
        }
        private static string GetColumnDataTypeFromAttribute(PropertyInfo info)
        {
            if (info.GetCustomAttributes(true).Any(X => X.GetType() == typeof(ColumnAttribute)))
            {
                var columnAttrri = info.GetCustomAttribute<ColumnAttribute>();
                if (columnAttrri != null)
                {
                    return columnAttrri.TypeName;
                }
            }
            return "";
        }
        private static string GetDeleteTableMigrationLine(string TableName, string Schema)
        {
            return @$" Delete.Table(""{TableName}"").InSchema(""{Schema}"");";
        }
        private static string GetAlterTableMigrationLine(string TableName, string Schema)
        {
            return @$" Alter.Table(""{TableName}"").InSchema(""{Schema}"") ";
        }
        private static string GetDeleteColumnMigrationLine(string ColumnName, string TableName, string Schema)
        {
            return @$"   Delete.Column(""{ColumnName}"").FromTable(""{TableName}"").InSchema(""{Schema}"");";
        }
        public static List<AppConfig> GetAppConfigs()
        {
            var result = new List<AppConfig>();
            if (File.Exists("appConfig.json"))
            {
                var data = File.ReadAllText("appConfig.json");
                if (!string.IsNullOrEmpty(data))
                {
                    result = JsonConvert.DeserializeObject<List<AppConfig>>(data);
                }
            }
            return result;
        }
        public static bool UpdateAppConfig(AppConfig config)
        {
            var data = GetAppConfigs();
            if (config.Id == Guid.Empty)
            {
                config.Id = Guid.NewGuid();
                config.Name = "config_" + (data.Count + 1).ToString();
                data.Add(config);
            }

            File.WriteAllText("appConfig.json", JsonConvert.SerializeObject(data));
            return true;
        }
    }

    public class TableDefinition : TableDefinitionBase
    {

        public string ColumnName { get; set; }
        public string DataType { get; set; }
        public string IsNullable { get; set; }
        public string IsIdentity { get; set; }
    }
    public class TableDefinitionBase
    {
        public string Schema { get; set; }
        public string TableName { get; set; }
    }
    public class AppConfig
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string ConnectionString { get; set; }
        public string Assembly { get; set; }
        public bool ConvertCamelToUnderscore { get; set; }
    }
    public class ColumnDefinition
    {
        public string ColumnName { get; set; }
        public string DataType { get; set; }
    }
}
