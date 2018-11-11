using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Recommendations;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Reflection;
using Microsoft.CodeAnalysis.Host.Mef;
using System.Composition.Hosting;
using System.Linq;
using Microsoft.CodeAnalysis.Scripting;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using System.Text;

namespace ConsoleApp10
{
    class AutoCompletionHandler : IAutoCompleteHandler
    {
        private readonly Func<string, int, string[]> _completionsProvider;

        public AutoCompletionHandler(Func<string, int, string[]> completionsProvider)
        {
            _completionsProvider = completionsProvider;
        }

        //public char[] Separators { get; set; } = "abcdefghjijklmnopqrstuvwxyz1234567890.()".ToCharArray();
        public char[] Separators { get; set; } = ".(,".ToCharArray();

        public string[] GetSuggestions(string text, int index)
        {
            var solution = Program.Current.Project.Solution;
            solution = solution.WithDocumentText(Program.Current.Id, SourceText.From(text));
            Program.Workspace.TryApplyChanges(solution);

            Program.Current = solution.GetDocument(Program.Current.Id);

            return _completionsProvider(text, index);
        }
    }

    class Program
    {
        public static Document Current { get; set; }
        public static AdhocWorkspace Workspace { get; private set; }

        internal static readonly IEnumerable<string> DefaultNamespaces = new[]
{
            "System",
            "System.IO",
            "System.Collections.Generic",
            "System.Console",
            "System.Diagnostics",
            "System.Dynamic",
            "System.Linq",
            "System.Linq.Expressions",
            "System.Text",
            "System.Threading.Tasks"
        };

        private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse, SourceCodeKind.Script);

        private static string GetWordToComplete(string text)
        {
            var builder = new StringBuilder();
            var reversedText = text.Reverse();

            foreach(var character in reversedText)
            {
                if (Char.IsLetterOrDigit(character))
                {
                    builder.Append(character);
                }
                else
                {
                    break;
                }
            }

            return new string(builder.ToString().Reverse().ToArray());
        }

        static async Task Main(string[] args)
        {
            ReadLine.AutoCompletionHandler = new AutoCompletionHandler((text, index) =>
            {
                var service = CompletionService.GetService(Current);
                var completions = service.GetCompletionsAsync(Current, text.Length-1).Result;

                var wordToComplete = GetWordToComplete(text);

                var result = completions.Items
                    .OrderByDescending(c => c.DisplayText.IsValidCompletionStartsWithExactCase(wordToComplete))
                    .ThenByDescending(c => c.DisplayText.IsValidCompletionStartsWithIgnoreCase(wordToComplete))
                    .ThenByDescending(c => c.DisplayText.IsCamelCaseMatch(wordToComplete))
                    .ThenByDescending(c => c.DisplayText.IsSubsequenceMatch(wordToComplete))
                    .ThenBy(c => c.DisplayText, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(c => c.FilterText, StringComparer.OrdinalIgnoreCase).Select(x =>
                    {
                        var displayText = x.DisplayText;
                        //if (x.Tags.Any())
                        //{
                        //    displayText = displayText + " #[" + string.Join(",", x.Tags.Select(t => t.ToLowerInvariant())) + "]";
                        //}

                        return displayText;
                    }).ToArray();

                return result;
            });

            var assemblies = new[]
            {
                Assembly.Load("Microsoft.CodeAnalysis"),
                Assembly.Load("Microsoft.CodeAnalysis.CSharp"),
                Assembly.Load("Microsoft.CodeAnalysis.Features"),
                Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features"),
            };

            var partTypes = MefHostServices.DefaultAssemblies.Concat(assemblies)
                    .Distinct()
                    .SelectMany(x => x.GetTypes())
                    .ToArray();

            var compositionContext = new ContainerConfiguration()
                .WithParts(partTypes)
                .CreateContainer();

            var host = MefHostServices.Create(compositionContext);

            Workspace = new AdhocWorkspace(host);

            var firstProj = Workspace.AddProject(CreateProject());
            Current = Workspace.AddDocument(CreateDocument(firstProj.Id, ""));

            ScriptState<object> scriptState = null;
            while (true)
            {
                var project = Workspace.AddProject(CreateProject(Current.Project));
                Current = Workspace.AddDocument(CreateDocument(project.Id, ""));

                Console.Write("> ");
                var input = ReadLine.Read();

                var solution = Current.Project.Solution;
                solution = solution.WithDocumentText(Current.Id, SourceText.From(input));
                Workspace.TryApplyChanges(solution);

                Current = solution.GetDocument(Current.Id);

                scriptState = scriptState == null ?
                    await CSharpScript.RunAsync(input) :
                    await scriptState.ContinueWithAsync(input);
            }


            //            var code = @"
            //namespace IntellisenseTest
            //{
            //    public class Class1
            //    {
            //        public static void Bar() { }

            //        public static void Foo(int n)
            //        {
            //            Ba         
            //        }
            //    }
            //}";
            //var sourceText = SourceText.From(code);



            //string projName = "NewProject";
            //var projectId = ProjectId.CreateNewId();
            //var versionStamp = VersionStamp.Create();
            //var projectInfo = ProjectInfo.Create(projectId, versionStamp, projName, projName, LanguageNames.CSharp);
            //var newProject = workspace.AddProject(projectInfo);
            //var newDocument = workspace.AddDocument(newProject.Id, "NewFile.cs", sourceText);

            //var service = CompletionService.GetService(newDocument);
            //var results = service.GetCompletionsAsync(newDocument, 169).Result;

            //var symbols = Recommender.GetRecommendedSymbolsAtPositionAsync(
            //    newDocument.GetSemanticModelAsync().Result,
            //    position,
            //    workspace).Result;

            //foreach(var i in results.Items.Where(x => !x.Tags.Contains("Keyword")))
            //{
            //    Console.WriteLine(i);
            //}

            //Console.ReadLine();
        }

        private static DocumentInfo CreateDocument(ProjectId projectId, string text)
        {
            var documentInfo = DocumentInfo.Create(
    DocumentId.CreateNewId(projectId), Guid.NewGuid() + ".csx",
    sourceCodeKind: SourceCodeKind.Script,
    loader: TextLoader.From(TextAndVersion.Create(SourceText.From(""), VersionStamp.Create())));

            return documentInfo;
        }

        private static ProjectInfo CreateProject(Project previous = null)
        {
            var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                usings: DefaultNamespaces);

            var random = Guid.NewGuid().ToString();
            var project = ProjectInfo.Create(
                filePath: "dummy",
                id: ProjectId.CreateNewId(),
                version: VersionStamp.Create(),
                name: random,
                assemblyName: $"{random}.dll",
                language: LanguageNames.CSharp,
                compilationOptions: compilationOptions,
                metadataReferences: GetDefaultReferences(),
                parseOptions: ParseOptions,
                isSubmission: true,
                hostObjectType: typeof(InteractiveScriptGlobals),
                projectReferences: previous != null ? new[] { new ProjectReference(previous.Id) } : null);

            return project;
        }

        private static HashSet<MetadataReference> GetDefaultReferences()
        {
            var assemblyReferences = new HashSet<MetadataReference>();
            var assemblies = new[]
            {
                typeof(object).GetTypeInfo().Assembly,
                typeof(Enumerable).GetTypeInfo().Assembly,
                typeof(Stack<>).GetTypeInfo().Assembly,
                typeof(Lazy<,>).GetTypeInfo().Assembly,
                FromName("System.Runtime"),
                FromName("mscorlib")
            };

            var references = assemblies
                .Where(a => a != null)
                .Select(a => a.Location)
                .Distinct()
                .Select(l =>
                {
                    return MetadataReference.CreateFromFile(l);
                });

            foreach (var reference in references)
            {
                assemblyReferences.Add(reference);
            }

            Assembly FromName(string assemblyName)
            {
                try
                {
                    return Assembly.Load(new AssemblyName(assemblyName));
                }
                catch
                {
                    return null;
                }
            }
            return assemblyReferences;
        }
    }

    public static class StringExtensions
    {
        public static bool IsValidCompletionFor(this string completion, string partial)
        {
            return completion.IsValidCompletionStartsWithIgnoreCase(partial) || completion.IsSubsequenceMatch(partial);
        }

        public static bool IsValidCompletionStartsWithExactCase(this string completion, string partial)
        {
            return completion.StartsWith(partial);
        }

        public static bool IsValidCompletionStartsWithIgnoreCase(this string completion, string partial)
        {
            return completion.ToLower().StartsWith(partial.ToLower());
        }

        public static bool IsCamelCaseMatch(this string completion, string partial)
        {
            return new string(completion.Where(c => c >= 'A' && c <= 'Z').ToArray()).StartsWith(partial.ToUpper());
        }

        public static bool IsSubsequenceMatch(this string completion, string partial)
        {
            if (partial == string.Empty)
            {
                return true;
            }

            if (partial.Length > 1 && completion.ToLowerInvariant().Contains(partial.ToLowerInvariant()))
            {
                return true;
            }

            // Limit the number of results returned by making sure
            // at least the first characters match.
            // We can get far too many results back otherwise.
            if (!FirstLetterMatches(partial, completion))
            {
                return false;
            }

            return new string(completion.ToUpper().Intersect(partial.ToUpper()).ToArray()) == partial.ToUpper();
        }

        private static bool FirstLetterMatches(string word, string match)
        {
            if (string.IsNullOrEmpty(match))
            {
                return false;
            }

            return char.ToLowerInvariant(word.First()) == char.ToLowerInvariant(match.First());
        }
    }
}
