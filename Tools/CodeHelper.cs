using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
namespace Tools
{
    public class CodeHelper
    {
        /// <summary>
        /// Returns true if classDeclaration has [DataContract] attribute
        /// </summary>
        /// <param name="classDeclaration"></param>
        /// <returns></returns>
        public bool HasDataContractAttributeV1(ClassDeclarationSyntax classDeclaration)
        {
            return classDeclaration.DescendantNodes()
                                   .OfType<AttributeListSyntax>()
                                   .Any(al => al.Attributes.Any(a => a.Name.NormalizeWhitespace().ToFullString() == "DataContract" ||
                                                                     a.Name.NormalizeWhitespace().ToFullString() == "DataContractAttribute"));
        }

        public bool HasDataContractAttributeV2(INamedTypeSymbol classSymbol)
        {
            return classSymbol.GetAttributes()
                .Any(a =>
                {
                    if (a.AttributeClass == null) return false;
                    return a.AttributeClass.ContainingSymbol + "." + a.AttributeClass.Name ==  "System.Runtime.Serialization.DataContractAttribute";
                });
        }
    }
}
