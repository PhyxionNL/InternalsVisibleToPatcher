using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using dnlib.DotNet;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace AssemblyPatcher;

public class PatcherTask : Task
{
    public ITaskItem[] AddVirtualTo { get; set; }

    [Required]
    public ITaskItem[] AssemblyNames { get; set; }

    [Required]
    public ITaskItem[] AttributeAssemblyNames { get; set; }

    [Required]
    public string IntermediateOutputPath { get; set; }

    public ITaskItem[] RemoveSealedFrom { get; set; }

    [Required]
    public ITaskItem[] SourceReferences { get; set; }

    public override bool Execute()
    {
        if (SourceReferences == null)
            throw new ArgumentNullException(nameof(SourceReferences));

        Directory.CreateDirectory(IntermediateOutputPath);

        var assemblies = new HashSet<string>();
        if (AssemblyNames != null)
            assemblies.UnionWith(AssemblyNames.Select(a => a.ItemSpec));

        if (RemoveSealedFrom != null)
            assemblies.UnionWith(RemoveSealedFrom.Select(a => a.ItemSpec));

        if (AddVirtualTo != null)
            assemblies.UnionWith(AddVirtualTo.Select(a => a.ItemSpec));

        foreach (string target in assemblies)
        {
            ITaskItem assembly = SourceReferences.FirstOrDefault(x => Path.GetFileNameWithoutExtension(GetFullFilePath(IntermediateOutputPath, x.ItemSpec)) == target);
            if (assembly == null)
                continue;

            string assemblyPath = GetFullFilePath(IntermediateOutputPath, assembly.ItemSpec);
            string targetAssemblyPath = Path.Combine(IntermediateOutputPath, Path.GetFileName(assemblyPath));
            var module = ModuleDefMD.Load(assemblyPath);

            // Add InternalsVisibleTo.
            if (AssemblyNames != null)
            {
                foreach (ITaskItem item in AssemblyNames.Where(x => x.ItemSpec == target))
                {
                    string assemblyName = item.GetMetadata("AssemblyName");
                    if (!string.IsNullOrEmpty(assemblyName))
                        AddInternalsVisibleToAttribute(module, assemblyName, targetAssemblyPath);
                }
            }

            // Remove sealed.
            if (RemoveSealedFrom != null)
            {
                foreach (ITaskItem item in RemoveSealedFrom.Where(x => x.ItemSpec == target))
                {
                    string typeNames = item.GetMetadata("TypeNames");
                    RemoveSealedFromTypes(module, targetAssemblyPath, typeNames);
                }
            }

            // Add virtual to members.
            if (AddVirtualTo != null)
            {
                foreach (ITaskItem item in AddVirtualTo.Where(x => x.ItemSpec == target))
                {
                    string memberNames = item.GetMetadata("MemberNames");
                    AddVirtualToMembers(module, targetAssemblyPath, memberNames);
                }
            }

            module.Write(targetAssemblyPath);
        }

        return true;
    }

    private void AddInternalsVisibleToAttribute(ModuleDefMD module, string assemblyName, string targetAssemblyPath)
    {
        TypeRef attrTypeRef = module.CorLibTypes.GetTypeRef("System.Runtime.CompilerServices", "InternalsVisibleToAttribute");
        var ctorSig = MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.String);
        var memberRef = new MemberRefUser(module, ".ctor", ctorSig, attrTypeRef);
        var customAttr = new CustomAttribute(memberRef);
        customAttr.ConstructorArguments.Add(new CAArgument(module.CorLibTypes.String, assemblyName));
        module.Assembly.CustomAttributes.Add(customAttr);
        Log.LogMessage(MessageImportance.Normal, $"Added 'InternalsVisibleToAttribute' for '{assemblyName}' in '{targetAssemblyPath}'");
    }

    private void RemoveSealedFromTypes(ModuleDefMD module, string targetAssemblyPath, string typeNames)
    {
        Func<string, bool> matches = CreateMatcher(typeNames);
        int count = 0;

        foreach (TypeDef type in module.Types)
        {
            if (type.IsSealed && matches(type.FullName))
            {
                type.IsSealed = false;
                count++;
            }
        }

        Log.LogMessage(MessageImportance.Normal, $"Removed 'sealed' from {count} types in '{targetAssemblyPath}' matching patterns: {typeNames}");
    }

    private void AddVirtualToMembers(ModuleDefMD module, string targetAssemblyPath, string memberNames)
    {
        Func<string, bool> matches = CreateMatcher(memberNames);
        int count = 0;

        foreach (TypeDef type in module.Types)
        {
            // Methods.
            foreach (MethodDef method in type.Methods)
            {
                if (matches(method.DeclaringType.FullName + "::" + method.Name))
                    MakeVirtual(method);
            }

            // Properties.
            foreach (PropertyDef prop in type.Properties)
            {
                if (matches(prop.DeclaringType.FullName + "::" + prop.Name))
                {
                    MakeVirtual(prop.GetMethod);
                    MakeVirtual(prop.SetMethod);
                }
            }

            // Events.
            foreach (EventDef evt in type.Events)
            {
                if (matches(evt.DeclaringType.FullName + "::" + evt.Name))
                {
                    MakeVirtual(evt.AddMethod);
                    MakeVirtual(evt.RemoveMethod);
                    MakeVirtual(evt.InvokeMethod);
                }
            }
        }

        void MakeVirtual(MethodDef method)
        {
            if (method is { IsVirtual: false, IsStatic: false, IsConstructor: false, IsAbstract: false, IsPrivate: false })
            {
                method.Attributes |= MethodAttributes.Virtual;
                method.Attributes &= ~MethodAttributes.Final;
                count++;
            }
        }

        Log.LogMessage(MessageImportance.Normal, $"Added 'virtual' to {count} members in '{targetAssemblyPath}' matching patterns: {memberNames}");
    }

    private static Func<string, bool> CreateMatcher(string patternsInput)
    {
        string[] patterns = (patternsInput ?? "*").Split([';'], StringSplitOptions.RemoveEmptyEntries)
                                                  .Select(x => x.Trim()).ToArray();

        Regex[] regexes = patterns.Select(x =>
        {
            string escaped = Regex.Escape(x).Replace("\\*", ".*");
            return new Regex($"^{escaped}$");
        }).ToArray();

        return value => regexes.Any(r => r.IsMatch(value));
    }

    private static string GetFullFilePath(string basePath, string path) => Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.Combine(basePath, path);
}