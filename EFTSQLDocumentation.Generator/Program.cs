using CommandLineParser.Arguments;
using CommandLineParser.Exceptions;
using System;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace EFTSQLDocumentation.Generator {
    class Program : IDisposable {
        static void Main(string[] args) {
            var parser = CreateParser();

            try {
                parser.ParseCommandLine(args);
            }
            catch (CommandLineException e) {
                Console.WriteLine(e.Message);
                parser.ShowUsage();

                return;
            }

            var connectionstring = ((ValueArgument<SqlConnectionStringBuilder>)parser.LookupArgument("connectionstring")).Value.ConnectionString;
            var inputFileName = ((FileArgument)parser.LookupArgument("input")).Value.FullName;
            var outputFileName = ((FileArgument)parser.LookupArgument("output")).Value != null ? ((FileArgument)parser.LookupArgument("output")).Value.FullName : inputFileName;
            var p = new Program(connectionstring, inputFileName, outputFileName);

            p.CreateDocumentation();
            p.Dispose();
        }

        private static CommandLineParser.CommandLineParser CreateParser() {
            var parser = new CommandLineParser.CommandLineParser();
            var connectionstringArgument = new ValueArgument<SqlConnectionStringBuilder>('c', "connectionstring", "Connectionstring of the documented database") {
                Optional = false,
                ConvertValueHandler = (stringValue) => {
                    SqlConnectionStringBuilder connectionstringBuilder;

                    try {
                        connectionstringBuilder = new SqlConnectionStringBuilder(stringValue);
                    }
                    catch {
                        throw new InvalidConversionException("invalid connection string", "connectionstring");
                    }

                    if (string.IsNullOrEmpty(connectionstringBuilder.InitialCatalog)) {
                        throw new InvalidConversionException("no InitialCatalog was specified", "connectionstring");
                    }

                    return connectionstringBuilder;
                }
            };
            var inputFileArgument = new FileArgument('i', "input", "original edmx file") { FileMustExist = true, Optional = false };
            var outputFileArgument = new FileArgument('o', "output", "output edmx file - Default : original edmx file") { FileMustExist = false, Optional = true };

            parser.Arguments.Add(connectionstringArgument);
            parser.Arguments.Add(inputFileArgument);
            parser.Arguments.Add(outputFileArgument);
            parser.IgnoreCase = true;

            return parser;
        }

        private SqlConnection _connection;

        public string Connectionstring { get; set; }
        public string InputFileName { get; set; }
        public string OutputFileName { get; set; }

        public Program(string connectionstring, string inputFileName, string outputFileName) {
            Connectionstring = connectionstring;
            InputFileName = inputFileName;
            OutputFileName = outputFileName;

            _connection = new SqlConnection(connectionstring);
            _connection.Open();
        }

        public void Dispose() {
            _connection.Dispose();
        }

        private void CreateDocumentation() {
            var doc = XDocument.Load(InputFileName);

            if (doc.Root == null) {
                throw new Exception(string.Format("Loaded XDocument Root is null. File: {0}", InputFileName));
            }

            var entityTypeElements = doc.FindByLocalName("EntityType");

            int i = 0;
            foreach (var entityTypeElement in entityTypeElements) {
                var tableName = entityTypeElement.Attribute("Name").Value;
                var propertyElements = entityTypeElement.FindByLocalName("Property");

                Console.WriteLine("Analyzing table {0} of {1}", i++, entityTypeElements.Count());
                Console.WriteLine(" => TableName : {0}" +
                                  "\n => property count : {1}", tableName, propertyElements.Count());
                Console.WriteLine(Environment.NewLine);

                AddNodeDocumentation(entityTypeElement, GetTableDocumentation(tableName));

                foreach (var propertyElement in propertyElements) {
                    var columnName = propertyElement.Attribute("Name").Value;

                    AddNodeDocumentation(propertyElement, GetColumnDocumentation(tableName, columnName));
                }
            }

            if (File.Exists(OutputFileName)) {
                File.Delete(OutputFileName);
            }

            doc.Save(OutputFileName);

            Console.WriteLine("Writing result to {0}", OutputFileName);
            Console.WriteLine(Environment.NewLine);

            #region add table and column summary
            var contextFileName = InputFileName.Replace(".edmx", ".Context.tt");

            if (File.Exists(contextFileName)) {
                var text = File.ReadAllText(contextFileName);

                text = text.Replace("<#=codeStringGenerator.DbSet(entitySet)#>", @"<#=""/// <summary>"" + Environment.NewLine + ""    "" + ""/// "" + ((entitySet.ElementType.Documentation != null) ? entitySet.ElementType.Documentation.Summary : """") + Environment.NewLine + ""    "" + ""/// </summary>"" + Environment.NewLine + ""    "" + codeStringGenerator.DbSet(entitySet)#>");

                File.WriteAllText(contextFileName, text);

                Console.WriteLine(@"Add table summary to {0}", contextFileName);
                Console.WriteLine(Environment.NewLine);
            }

            var entityFileName = InputFileName.Replace(".edmx", ".tt");

            if (File.Exists(entityFileName)) {
                var text = File.ReadAllText(entityFileName);

                text = text.Replace("<#=codeStringGenerator.EntityClassOpening(entity)#>", @"<#=""/// <summary>"" + Environment.NewLine + ""/// "" + ((entity.Documentation != null) ? entity.Documentation.Summary : """") + Environment.NewLine + ""/// </summary>"" + Environment.NewLine + codeStringGenerator.EntityClassOpening(entity)#>");
                text = text.Replace("<#=codeStringGenerator.Property(edmProperty)#>", @"<#=""/// <summary>"" + Environment.NewLine + ""    "" + ""/// "" + ((edmProperty.Documentation != null) ? edmProperty.Documentation.Summary : """") + Environment.NewLine + ""    "" + ""/// </summary>"" + Environment.NewLine + ""    "" + codeStringGenerator.Property(edmProperty)#>");
                text = text.Replace("<#=codeStringGenerator.Property(complexProperty)#>", @"<#=""/// <summary>"" + Environment.NewLine + ""    "" + ""/// "" + ((complexProperty.Documentation != null) ? complexProperty.Documentation.Summary : """") + Environment.NewLine + ""    "" + ""/// </summary>"" + Environment.NewLine + ""    "" + codeStringGenerator.Property(complexProperty)#>");

                File.WriteAllText(entityFileName, text);

                Console.WriteLine(@"Add column summary to {0}", entityFileName);
                Console.WriteLine(Environment.NewLine);
            }

            if (File.Exists(contextFileName) && File.Exists(entityFileName)) {
                Console.WriteLine(@"Please run ""Custom Tool"" to generate summary");
                Console.WriteLine(@"Context => ""{0}""", Path.GetFileName(contextFileName));
                Console.WriteLine(@"Entitys => ""{0}""", Path.GetFileName(entityFileName));
                Console.WriteLine(Environment.NewLine);
            }

            Console.WriteLine(@"Operation is completed");
            #endregion
        }

        private void AddNodeDocumentation(XElement element, string documentation) {
            element.FindByLocalName("Documentation").Remove();

            if (string.IsNullOrEmpty(documentation)) {
                return;
            }

            var xmlns = element.GetDefaultNamespace();

            element.AddFirst(new XElement(xmlns + "Documentation", new XElement(xmlns + "Summary", documentation)));
        }

        private string GetTableDocumentation(string tableName) {
            using (var command = new SqlCommand(@"
                SELECT [value] 
                FROM fn_listextendedproperty (
                    'MS_Description', 
                    'schema', 'dbo', 
                    'table',  @TableName, 
                    null, null)", _connection)) {
                command.Parameters.AddWithValue("TableName", tableName);

                return command.ExecuteScalar() as string;
            }
        }

        private string GetColumnDocumentation(string tableName, string columnName) {
            using (var command = new SqlCommand(@"
                SELECT [value] 
                FROM fn_listextendedproperty (
                    'MS_Description', 
                    'schema', 'dbo', 
                    'table', @TableName, 
                    'column', @columnName)", _connection)) {
                command.Parameters.AddWithValue("TableName", tableName);
                command.Parameters.AddWithValue("ColumnName", columnName);

                return command.ExecuteScalar() as string;
            }
        }
    }
}
