using System.Windows.Markup;
using Hublot.Common;

namespace HublotCore;

/// <summary>
/// Texte traduit dans le XAML : <c>Text="{local:Tr Cle}"</c>. Les textes (français et anglais) sont dans
/// Common/Strings.cs ; la langue est fixée à l'ouverture de la fenêtre.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class Tr : MarkupExtension
{
    public Tr() { }

    public Tr(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Key);
}
