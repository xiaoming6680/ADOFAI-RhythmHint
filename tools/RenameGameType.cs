#:package Mono.Cecil@0.11.6
#:property TargetFramework=net10.0
#:property PublishAot=false

// 模拟旧版游戏：复制一份 Assembly-CSharp，把其中一个类型改名后写出。游戏自己按类型令牌引用不受影响，
// 本 Mod 按名字引用，加载时找不到，和旧版游戏缺这个类型时报同一个错（2026-11-27 玩家截图：游戏 3.4.0 之前没有 HitMarginPerfectTextPreset）。
// 用法：
//   dotnet run tools/RenameGameType.cs -- <原 Assembly-CSharp.dll> <输出.dll> <类型全名>
// 步骤见 docs/开发说明.md「判定提示」的旧版游戏一条：先备份游戏的 Assembly-CSharp.dll，用原版编译 Mod 并装进游戏，
// 再把输出文件换进游戏的 Managed 文件夹开游戏；测完换回原版、核对 SHA-256。
// 换着模拟版时不要编译 Mod：Mod 是对着 Managed 里的程序集编译的，会直接引用改名后的类型，测不出问题。

using Mono.Cecil;

if (args.Length != 3)
{
    Console.Error.WriteLine("用法：dotnet run tools/RenameGameType.cs -- <原 Assembly-CSharp.dll> <输出.dll> <类型全名>");
    return 2;
}

var (source, output, typeName) = (Path.GetFullPath(args[0]), Path.GetFullPath(args[1]), args[2]);
if (!File.Exists(source))
{
    Console.Error.WriteLine($"找不到 {source}");
    return 1;
}

if (string.Equals(source, output, StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("输出文件不能和原文件相同：先备份原版，改出来的写到别处");
    return 1;
}

// 依赖（DOTween 等）和原文件在同一个 Managed 文件夹
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(source)!);
using var assembly = AssemblyDefinition.ReadAssembly(source, new ReaderParameters { AssemblyResolver = resolver });
var type = assembly.MainModule.GetType(typeName);
if (type == null)
{
    Console.Error.WriteLine($"{source} 里没有类型 {typeName}");
    return 1;
}

type.Name += "_RenamedForTest";
assembly.Write(output);
Console.WriteLine($"已写出 {output}：{typeName} 改名为 {type.FullName}");
return 0;
