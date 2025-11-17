# AssemblyPatcher

An MSBuild task that adds [InternalsVisibleTo](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.internalsvisibletoattribute) attributes or removes sealed modifiers from existing assemblies.

# Usage
First add the [NuGet package](https://www.nuget.org/packages/AssemblyPatcher) to your project:
```xml
<PackageReference Include="AssemblyPatcher">
   <PrivateAssets>all</PrivateAssets>
   <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```

To add an ```InternalsVisibleToAttribute``` to ```SomeAssembly``` with ```OwnAssembly``` as assembly name, define an ```ItemGroup``` with the following:
```xml
<ItemGroup>
   <AddInternalsVisibleTo Include="SomeAssembly" AssemblyName="OwnAssembly" />
</ItemGroup>
```

To remove sealed modifiers from types in ```SomeAssembly```, define an ```ItemGroup``` with the following:
```xml
<ItemGroup>
   <RemoveSealedFrom Include="SomeAssembly" />
</ItemGroup>
```

You can also filter the types:
```xml
<ItemGroup>
   <RemoveSealedFrom Include="SomeAssembly" TypeNames="SomeAssembly.TypeA;SomeAssembly.Folder.*" />
</ItemGroup>
```

To add virtual modifier to types in ```SomeAssembly```, define an ```ItemGroup``` with the following:
```xml
<ItemGroup>
   <AddVirtualTo Include="SomeAssembly" />
</ItemGroup>
```

You can also filter the types (uses dnlib notiation):
```xml
<ItemGroup>
   <AddVirtualTo Include="SomeAssembly" MemberNames="SomeAssembly.TypeA;SomeAssembly.Folder::Member*" />
</ItemGroup>
```

# License
MIT
