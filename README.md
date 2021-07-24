# Unit Testing Roslyn Methods (Code Analysis)
While I was working on a .NET Standard 2.1 project I created some Roslyn methods to extract information from the document that I was trying to analyze. The idea was to detect if a given class has the attribute **[DataContract]** added or not.

## Initial Version

```csharp
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

/// <summary>
/// Returns true if classDeclaration has [DataContract] attribute
/// </summary>
/// <param name="classDeclaration"></param>
/// <returns></returns>
public bool HasDataContractAttributeV1(ClassDeclarationSyntax classDeclaration)
{
    return classDeclaration.DescendantNodes()
                           .OfType<AttributeListSyntax>()
                           .Any(al => al.Attributes
                              .Any(a => a.Name.NormalizeWhitespace().ToFullString() == "DataContract" ||
                                        a.Name.NormalizeWhitespace().ToFullString() == "DataContractAttribute"));
}
```
To be sure the method works ok I created a test project:
```csharp
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
```
Accorging to the test this method is working. But it has a potential problem: any attribute with the name **[DataContract]** will pass the test. That's is not what I wanted. I would like to detect if the document I am analyzing has the attribute of type **System.Runtime.Serialization.DataContractAttribute**.
## Version 2
So, I decided to work with semantical instead of syntactical information and I modified the previous method to accomplish the same goal using a different methodology.
```csharp
public bool HasDataContractAttributeV2(INamedTypeSymbol classSymbol)
{
    return classSymbol.GetAttributes()
        .Any(a =>
        {
            if (a.AttributeClass == null) return false;
            return a.AttributeClass.ContainingSymbol + "." + a.AttributeClass.Name ==  "System.Runtime.Serialization.DataContractAttribute";
        });
}
```
Now it's time to test if the new method is working. There can't be hard, Isn't it?

## Well... Lets see
First, it's necessary to create a workspace to work with because the semantical model requires some extra steps before you can use it.
The workspace type that I used is [MSBuildWorspace](https://github.com/dotnet/roslyn/blob/main/src/Workspaces/Core/MSBuild/MSBuild/MSBuildWorkspace.cs). To use it, I needed to add these nugget packages into my test project:
- **Microsoft.Build.Locator**
- **Microsoft.CodeAnalysis.Workspaces.MSBuild**
```csharp
private const string TestingPrjName = "TestingProject";
private const string TestingClassFileName = "TestingClass.cs";

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
```
Then I created a workspace instance and prepare the solution for testing:
```csharp
[TestMethod]
public async Task HasDataContractAttributeV2_ShouldReturnTrue_WhenClassHasThisAttribute()
{
    var ws = MSBuildWorkspace.Create();
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
        
    // ...
}
```
## Some things to take into account:
- I created a new **MSBuildWorkspace**
- Then I added a new project called **TestingProject**
- Then I added a new document inside the project. This document has the same source code that I used in the first test
- The project's output is a **dll** (classlib) and the language is **C#**

It's time to compile this project in order to see if everything is ok:
```csharp
var comp = await prj.GetCompilationAsync();
if (comp.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
    throw new InvalidOperationException("There are compilation errors.");
```

## Oops.. it's not working
When I ran this test I got an <strong>InvalidOperationException </strong>because there weren't references added to the project. So I needed to add them:
```csharp
ImmutableArray<MetadataReference> prjRef =
    ImmutableArray.Create<MetadataReference>(
        MetadataReference.CreateFromFile(typeof(string).Assembly.Location),
        MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Runtime.Serialization.Primitives").Location)
);

sln = sln.Projects.First(p => p.Id == prjId).AddMetadataReferences(prjRef).Solution;
```
After that, the compilation passed without errors. It's time to get semantical information:
```csharp
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
```

Finally, I tested the method:
```csharp
var sut = new CodeHelper();
Assert.IsTrue(sut.HasDataContractAttributeV2(classSymbol));
```
**The test passed OK!**

## Some recommendations
- If you are not getting the semantic information, for example your method **GetCompilationAsync** is returning null, it is possible you ommited some **Microsoft.CodeAnalysis.** nuget packages that are necessary to make thinks work.
- Another good tip is to attach an event handler on the workspace in order to detect any error:
```csharp
var ws = MSBuildWorkspace.Create();
ws.WorkspaceFailed += (server, eventArgs) => {
    if (eventArgs.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure) 
        throw new InvalidOperationException(eventArgs.Diagnostic.Message);
};
```
- Remember to add all references needed for the semantical analysis. For example, in my case, I added the reference to the assembly **System.Runtime.Serialization.Primitives** because this module contains the attribute <strong>DataContract </strong>that I was looking for

I hope this indications can help you out in the testing process. Enjoy it!
