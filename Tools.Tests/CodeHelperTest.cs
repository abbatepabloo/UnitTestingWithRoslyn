using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Tools.Tests
{

    [TestClass]
    public class CodeHelperTest
    {
        private const string TestingPrjName = "TestingProject";
        private const string TestingClassFileName = "TestingClass.cs";
        private const string TestToCode = @"
using System;
using System.Runtime.Serialization;
namespace TestingProject
{
    [DataContract] // <- This is the attribute we would like to find
    public class TestingClass
    {
    }
}";

        [TestInitialize]
        public void TestInitialization()
        {
            if (!MSBuildLocator.IsRegistered)
            {
                MSBuildLocator.RegisterDefaults();
            }
        }

        [TestCleanup]
        public void TestCleanUp()
        {
            if (MSBuildLocator.IsRegistered)
            {
                MSBuildLocator.Unregister();
            }
        }

        [TestMethod]
        public void HasDataContractAttributeV1_ShouldReturnTrue_WhenClassHasThisAttribute()
        {
            var tree = CSharpSyntaxTree.ParseText(TestToCode);
            var classDeclaration = tree.GetRoot().DescendantNodes()
                    .OfType<ClassDeclarationSyntax>()
                    .First();
            var sut = new CodeHelper();

            Assert.IsTrue(sut.HasDataContractAttributeV1(classDeclaration));
        }

        [TestMethod]
        public async Task HasDataContractAttributeV2_ShouldReturnTrue_WhenClassHasThisAttribute()
        {

            var ws = MSBuildWorkspace.Create();
            ws.WorkspaceFailed += (server, eventArgs) => {
                if (eventArgs.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure) 
                    throw new InvalidOperationException(eventArgs.Diagnostic.Message);
            };

            var prjId = ProjectId.CreateNewId();
            var pInfo = ProjectInfo.Create(prjId,
                VersionStamp.Create(),
                TestingPrjName,
                TestingPrjName,
                LanguageNames.CSharp, null, null,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            Solution sln = ws.CurrentSolution.AddProject(pInfo);
            var prj = sln.GetProject(prjId);

            SourceText sourceText = SourceText.From(TestToCode);
            sln = sln.GetProject(prjId).AddDocument(TestingClassFileName, sourceText)
                .Project
                .Solution;

            #region References
            ImmutableArray<MetadataReference> prjRef =
                ImmutableArray.Create<MetadataReference>(
                    MetadataReference.CreateFromFile(typeof(string).Assembly.Location),
                    MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location),
                    MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
                    MetadataReference.CreateFromFile(Assembly.Load("System.Runtime.Serialization.Primitives").Location)
            );

            sln = sln.Projects.First(p => p.Id == prjId).AddMetadataReferences(prjRef).Solution;
            #endregion

            // Getting project
            prj = sln.GetProject(prjId);

            // Getting compilation and checking if there are errors.
            // This step is optional, but sometimes it can drive you crazy if you don't detect any compilation error
            var comp = await prj.GetCompilationAsync();
            if (comp.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
                throw new InvalidOperationException("There are compilation errors.");

            // Getting the project's document under test
            var doc = prj.Documents.First(d => d.Name == TestingClassFileName);
            // Getting SyntaxRoot
            var sRoot = await doc.GetSyntaxRootAsync();
            // Look for the class declaration
            var classDec = sRoot.DescendantNodes().OfType<ClassDeclarationSyntax>().First();
            // Getting semantic model of document
            var sm = await doc.GetSemanticModelAsync();
            // Getting the the class's symbol
            var classSymbol = sm.GetDeclaredSymbol(classDec);

            var sut = new CodeHelper();
            Assert.IsTrue(sut.HasDataContractAttributeV2(classSymbol));
        }
    }
}
