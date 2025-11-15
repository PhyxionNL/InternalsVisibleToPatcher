using System;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace InternalsVisibleToPatcher;

public class PatcherTask : Task
{
    [Required]
    public ITaskItem[] AssemblyNames { get; set; }

    [Required]
    public ITaskItem[] AttributeAssemblyNames { get; set; }

    [Required]
    public string IntermediateOutputPath { get; set; }

    [Required]
    public ITaskItem[] SourceReferences { get; set; }

    public override bool Execute()
    {
        if (SourceReferences == null)
            throw new ArgumentNullException(nameof(SourceReferences));

        if (AssemblyNames.Length == 0 || AttributeAssemblyNames.Length == 0)
            return true;

        Directory.CreateDirectory(IntermediateOutputPath);

        for (int i = 0; i < AssemblyNames.Length && i < AttributeAssemblyNames.Length; i++)
        {
            string target = AssemblyNames[i].ItemSpec;
            string assemblyName = AttributeAssemblyNames[i].ItemSpec;
            if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(assemblyName))
                continue;

            ITaskItem assembly = SourceReferences.FirstOrDefault(a => Path.GetFileNameWithoutExtension(GetFullFilePath(IntermediateOutputPath, a.ItemSpec)) == target);
            if (assembly != null)
            {
                string assemblyPath = GetFullFilePath(IntermediateOutputPath, assembly.ItemSpec);
                string targetAssemblyPath = Path.Combine(IntermediateOutputPath, Path.GetFileName(assemblyPath));
                AddInternalsVisibleToAttribute(assemblyPath, targetAssemblyPath, assemblyName);
                Log.LogMessageFromText($"Added InternalsVisibleToAttribute for '{assemblyName}' in '{targetAssemblyPath}'", MessageImportance.Normal);
            }
        }

        return true;
    }

    private static void AddInternalsVisibleToAttribute(string source, string target, string inputAssemblyName)
    {
        var module = ModuleDefMD.Load(source);
        TypeRef attrTypeRef = module.CorLibTypes.GetTypeRef("System.Runtime.CompilerServices", "InternalsVisibleToAttribute");
        var ctorSig = MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.String);
        var memberRef = new MemberRefUser(module, ".ctor", ctorSig, attrTypeRef);
        var customAttr = new CustomAttribute(memberRef);
        customAttr.ConstructorArguments.Add(new CAArgument(module.CorLibTypes.String, inputAssemblyName));
        module.Assembly.CustomAttributes.Add(customAttr);
        module.Write(target);
    }

    private static string GetFullFilePath(string basePath, string path) => Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.Combine(basePath, path);
}