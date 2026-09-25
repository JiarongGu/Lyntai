namespace Lyntai.Tests.Fakes;

/// <summary>An npm/nvm-style launcher layout: an EXTENSIONLESS POSIX shim, which CreateProcess refuses to
/// spawn, beside the Windows sibling(s) a spawn must resolve to instead.</summary>
public static class WindowsShim
{
    /// <summary>Writes <paramref name="name"/> and each sibling (<c>name + extension</c>) into
    /// <paramref name="dir"/>; returns the extensionless shim's path.</summary>
    public static string Write(ScratchDir dir, string name, params (string Extension, string Content)[] siblings)
    {
        var shim = dir.File(name, "#!/bin/sh\nexec node \"$0.mjs\" \"$@\"\n");   // a real npm shim: sh, not PE
        foreach (var (extension, content) in siblings)
            dir.File(name + extension, content);
        return shim;
    }

    /// <summary>A <c>.cmd</c> sibling that runs the repository's provider stub with the shim's arguments.</summary>
    public static (string Extension, string Content) CmdRunningTheProviderStub() =>
        (".cmd", $"@echo off\r\nnode \"{Path.Combine(TestPaths.DevtoolsDir("scripts"), "provider-stub.mjs")}\" %*\r\n");
}
