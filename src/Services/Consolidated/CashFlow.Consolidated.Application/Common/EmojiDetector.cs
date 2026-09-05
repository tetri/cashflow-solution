using System.Text.RegularExpressions;

namespace CashFlow.Consolidated.Application.Common;

/// <summary>
/// Componente utilitario de seguranca e saneamento para deteccao de emojis em textos e rotas.
/// Em estrita conformidade com a diretriz R3, o sistema rejeita terminantemente emojis
/// nos identificadores de comerciantes e outros campos textuais.
/// </summary>
public static class EmojiDetector
{
    private static readonly Regex PadraoEmojis = new Regex(
        @"(\u00a9|\u00ae|[\u2600-\u26ff]|[\u2700-\u27bf]|[\u2300-\u23ff]|[\u2b50-\u2b55]|\ud83c[\ud000-\udfff]|\ud83d[\ud000-\udfff]|\ud83e[\ud000-\udfff])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    private static readonly Regex PadraoEscapeJsonEmoji = new Regex(
        @"(\\u[dD][89a-bA-B][0-9a-fA-F]{2}\\u[dD][c-fC-F][0-9a-fA-F]{2}|\\u2[67][0-9a-fA-F]{2}|\\u[dD]83[c-eC-E])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    /// <summary>
    /// Avalia se a cadeia de caracteres informada contem algum caractere classificado como emoji.
    /// </summary>
    /// <param name="texto">Texto a ser inspecionado.</param>
    /// <returns>Verdadeiro se ao menos um emoji for identificado; caso contrario, falso.</returns>
    public static bool ContemEmoji(string? texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return false;
        }

        if (PadraoEscapeJsonEmoji.IsMatch(texto))
        {
            return true;
        }

        if (PadraoEmojis.IsMatch(texto))
        {
            return true;
        }

        for (var i = 0; i < texto.Length; i++)
        {
            var codePoint = char.ConvertToUtf32(texto, i);
            if (char.IsSurrogatePair(texto, i))
            {
                i++;
            }

            if (codePoint >= 0x1F600 && codePoint <= 0x1F64F)
            {
                return true;
            }

            if (codePoint >= 0x1F300 && codePoint <= 0x1F5FF)
            {
                return true;
            }

            if (codePoint >= 0x1F680 && codePoint <= 0x1F6FF)
            {
                return true;
            }

            if (codePoint >= 0x1F700 && codePoint <= 0x1F77F)
            {
                return true;
            }

            if (codePoint >= 0x1F780 && codePoint <= 0x1F7FF)
            {
                return true;
            }

            if (codePoint >= 0x1F800 && codePoint <= 0x1F8FF)
            {
                return true;
            }

            if (codePoint >= 0x1F900 && codePoint <= 0x1F9FF)
            {
                return true;
            }

            if (codePoint >= 0x1FA00 && codePoint <= 0x1FA6F)
            {
                return true;
            }

            if (codePoint >= 0x1FA70 && codePoint <= 0x1FAFF)
            {
                return true;
            }

            if (codePoint >= 0x2600 && codePoint <= 0x26FF)
            {
                return true;
            }

            if (codePoint >= 0x2700 && codePoint <= 0x27BF)
            {
                return true;
            }
        }

        return false;
    }
}
