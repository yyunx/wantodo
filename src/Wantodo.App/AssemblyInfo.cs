using System.Windows;

// 探针 spike/FreeformProbe 要直接驱动 FreeformBoard 的内部手势路径（笔迹落盘等），
// 只为自动验收服务；产品代码不依赖探针。
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("FreeformProbe")]

[assembly:ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]
