using System;
using System.Reflection;
using CommandLine;

namespace FluentMigration.Generator.PostgreSql
{
    class Program
    {
        private class Options
        {
            public Options(string assembly, string connectionString)
            {
                Assembly = assembly;
                ConnectionString = connectionString;
            }

            [Option('c', "connectionstring", Required = false, HelpText = "Database connection string.")]
            public string ConnectionString { get;  }
            [Option('a', "Assembly", Required = false, HelpText = "Assembly location.")]
            public string Assembly { get;  }
        }

    

        static void Main(string[] args)
        {
            try
            {

                var cStr = "";
                var assemblyLocation = "";
                const bool updateAppConfig = false;
                Parser.Default.ParseArguments<Options>(args)
                    .WithParsed<Options>(o =>
                    {
                        cStr = o.ConnectionString;
                        assemblyLocation = o.Assembly;
                    });
                if (string.IsNullOrEmpty(cStr))
                {
                    Console.WriteLine("Enter database connection string");
                    cStr = Console.ReadLine();
                }

                if (string.IsNullOrEmpty(assemblyLocation))
                {
                    Console.WriteLine("Enter assembly");
                    assemblyLocation = Console.ReadLine();
                }

                if (string.IsNullOrEmpty(cStr) || string.IsNullOrEmpty(assemblyLocation))
                {
                    Console.WriteLine("Assembly and database connection details  both are required");
                    return;
                }


                var config = GenerateMigrationHelper.GetAppConfigs();
                GenerateMigrationHelper.UpdateAppConfig(new AppConfig
                    {ConnectionString = cStr, Assembly = assemblyLocation});

                // Console.WriteLine(cStr);
                var assembly = Assembly.LoadFrom(assemblyLocation);
                GenerateMigrationHelper.Process(cStr, assembly);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }
}
