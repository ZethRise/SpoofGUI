namespace SpoofGUI.GUI.ViewModels;

public sealed class ShellViewModel
{
    public string TitleBarLine(string state, string? flow = null) => state switch
    {
        "live"        => $"SpoofGUI — live · {flow}",
        "establishing" => "SpoofGUI — establishing",
        _              => "SpoofGUI — idle",
    };
}
