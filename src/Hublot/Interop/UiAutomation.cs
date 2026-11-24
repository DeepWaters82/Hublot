using System.Runtime.InteropServices;

namespace Hublot.Interop;

/// <summary>
/// Client UI Automation minimal (UIAutomationCore.dll) : lire l'arbre d'une fenêtre d'une autre application
/// (nom, identifiant, classe de chaque élément) et « cliquer » un bouton (motif Invoke). COM appelé par la table de
/// méthodes, comme <see cref="ShellItems"/> : pas de génération de code, compatible trimming. Indices des méthodes
/// relevés dans UIAutomationClient.h (SDK 10.0.26100).
///
/// À n'utiliser que depuis un thread de travail initialisé en multithread (<see cref="CoInitializeEx"/>) : un appel
/// vers une application qui ne répond plus peut bloquer plusieurs secondes, jamais le thread de l'interface.
/// Chaque élément renvoyé est une référence COM à libérer par <see cref="Release"/>.
/// </summary>
internal sealed unsafe partial class UiaClient : IDisposable
{
    private static readonly Guid CLSID_CUIAutomation = new("ff48dba4-60ef-4201-aa87-54103eef594e");
    private static readonly Guid IID_IUIAutomation = new("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee");
    private static readonly Guid IID_IUIAutomationInvokePattern = new("fb377fbe-8ea6-46d5-9c73-6499642d3059");
    private const int UIA_InvokePatternId = 10000;
    private const uint CLSCTX_INPROC_SERVER = 1;
    public const uint COINIT_MULTITHREADED = 0;

    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(nint reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    public static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* instance);

    [LibraryImport("oleaut32.dll")]
    private static partial void SysFreeString(nint bstr);

    private nint _automation, _walker;

    private UiaClient(nint automation, nint walker) => (_automation, _walker) = (automation, walker);

    /// <summary>Client UI Automation et parcours de l'arbre brut (« raw view » : tous les éléments), ou null.</summary>
    public static UiaClient? Create()
    {
        nint automation, walker;
        var clsid = CLSID_CUIAutomation;
        var iid = IID_IUIAutomation;
        if (CoCreateInstance(&clsid, 0, CLSCTX_INPROC_SERVER, &iid, &automation) != 0 || automation == 0) return null;
        // IUIAutomation::get_RawViewWalker (méthode 16)
        if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)automation)[16])(automation, &walker) != 0 || walker == 0)
        {
            Release(automation);
            return null;
        }
        return new UiaClient(automation, walker);
    }

    /// <summary>Élément racine d'une fenêtre, ou 0.</summary>
    public nint FromHandle(nint hwnd)
    {
        nint element;
        // IUIAutomation::ElementFromHandle (méthode 6)
        return ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)(*(nint**)_automation)[6])(_automation, hwnd, &element) == 0 ? element : 0;
    }

    /// <summary>Premier enfant, ou 0.</summary>
    public nint FirstChild(nint element) => Walk(element, 4); // IUIAutomationTreeWalker::GetFirstChildElement

    /// <summary>Élément suivant de même parent, ou 0.</summary>
    public nint NextSibling(nint element) => Walk(element, 6); // IUIAutomationTreeWalker::GetNextSiblingElement

    private nint Walk(nint element, int method)
    {
        nint result;
        return ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)(*(nint**)_walker)[method])(_walker, element, &result) == 0 ? result : 0;
    }

    /// <summary>Nom affiché de l'élément (texte, libellé du bouton…).</summary>
    public static string Name(nint element) => ReadString(element, 23);           // get_CurrentName

    /// <summary>Identifiant d'automatisation (stable, indépendant de la langue : « eltSummary »…).</summary>
    public static string AutomationId(nint element) => ReadString(element, 29);   // get_CurrentAutomationId

    public static string ClassName(nint element) => ReadString(element, 30);      // get_CurrentClassName

    public const int ButtonControlType = 50000; // UIA_ButtonControlTypeId

    /// <summary>Type de contrôle (UIA_ButtonControlTypeId = 50000…), 0 si inconnu.</summary>
    public static int ControlType(nint element)
    {
        int type = 0;
        // IUIAutomationElement::get_CurrentControlType (méthode 21)
        return ((delegate* unmanaged[Stdcall]<nint, int*, int>)(*(nint**)element)[21])(element, &type) == 0 ? type : 0;
    }

    private static string ReadString(nint element, int method)
    {
        nint bstr = 0;
        if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)element)[method])(element, &bstr) != 0 || bstr == 0) return "";
        // BSTR : longueur en octets juste avant le premier caractère.
        var text = new string((char*)bstr, 0, *(int*)(bstr - 4) / 2);
        SysFreeString(bstr);
        return text;
    }

    /// <summary>« Clique » l'élément (motif Invoke) ; faux s'il ne le permet pas.</summary>
    public static bool Invoke(nint element)
    {
        nint pattern;
        var iid = IID_IUIAutomationInvokePattern;
        // IUIAutomationElement::GetCurrentPatternAs (méthode 14)
        if (((delegate* unmanaged[Stdcall]<nint, int, Guid*, nint*, int>)(*(nint**)element)[14])(element, UIA_InvokePatternId, &iid, &pattern) != 0 || pattern == 0)
            return false;
        // IUIAutomationInvokePattern::Invoke (méthode 3)
        int hr = ((delegate* unmanaged[Stdcall]<nint, int>)(*(nint**)pattern)[3])(pattern);
        Release(pattern);
        return hr == 0;
    }

    public static void Release(nint unknown)
    {
        if (unknown != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)unknown)[2])(unknown);
    }

    public void Dispose()
    {
        Release(_walker);
        Release(_automation);
        _walker = _automation = 0;
    }
}

/// <summary>Événements du système sur les fenêtres (SetWinEventHook), reçus par la boucle de messages du thread UI.</summary>
internal static unsafe partial class WinEvents
{
    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000, WINEVENT_SKIPOWNPROCESS = 0x0002;
    public const int OBJID_WINDOW = 0;

    [LibraryImport("user32.dll")]
    public static partial nint SetWinEventHook(uint eventMin, uint eventMax, nint module,
        delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> callback, uint processId, uint threadId, uint flags);

    [LibraryImport("user32.dll")]
    public static partial int UnhookWinEvent(nint hook);

    [LibraryImport("user32.dll")]
    public static partial int IsWindow(nint hwnd);
}
