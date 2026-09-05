using System.Text.RegularExpressions;

namespace CashFlow.E2ETests.Infraestrutura;

/// <summary>
/// Utilitario para deteccao e validacao de ausencia total de emojis em textos e objetos.
/// Garante o cumprimento estrito da regra de negocio de zero emojis em todo o ecossistema,
/// sem gerar falsos-positivos com caracteres de pontuacao ou acentos da lingua portuguesa.
/// </summary>
public static class VerificadorEmojis
{
    // Faixas Unicode especificas correspondentes a emojis e simbolos graficos
    private static readonly Regex PadraoEmojis = new Regex(
        @"(\u00a9|\u00ae|[\u2600-\u26ff]|[\u2700-\u27bf]|[\u2300-\u23ff]|[\u2b50-\u2b55]|\ud83c[\ud000-\udfff]|\ud83d[\ud000-\udfff]|\ud83e[\ud000-\udfff])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    // Deteccao de sequencias de escape JSON Unicode representativas de emojis
    private static readonly Regex PadraoEscapeJsonEmoji = new Regex(
        @"(\\u[dD][89a-bA-B][0-9a-fA-F]{2}\\u[dD][c-fC-F][0-9a-fA-F]{2}|\\u2[67][0-9a-fA-F]{2}|\\u[dD]83[c-eC-E])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    /// <summary>
    /// Verifica se a cadeia de caracteres informada contem algum caractere emoji (literal ou escapado).
    /// </summary>
    /// <param name="texto">Texto a ser inspecionado.</param>
    /// <returns>Verdadeiro se contiver emoji; caso contrario, falso.</returns>
    public static bool ContemEmoji(string? texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return false;
        }

        // Checagem via sequencias de escape JSON
        if (PadraoEscapeJsonEmoji.IsMatch(texto))
        {
            return true;
        }

        // Checagem via expressao regular para simbolos suplementares
        if (PadraoEmojis.IsMatch(texto))
        {
            return true;
        }

        // Inspecao individual de code points para cobrir grafemas compostos
        for (var i = 0; i < texto.Length; i++)
        {
            var codePoint = char.ConvertToUtf32(texto, i);
            if (char.IsSurrogatePair(texto, i))
            {
                i++; // Pula o par de substitutos
            }

            // Intervalos conhecidos de emojis no padrao Unicode com chaves estritas (IDE0011)
            if (codePoint >= 0x1F600 && codePoint <= 0x1F64F)
            {
                return true; // Emoticons
            }

            if (codePoint >= 0x1F300 && codePoint <= 0x1F5FF)
            {
                return true; // Simbolos diversos
            }

            if (codePoint >= 0x1F680 && codePoint <= 0x1F6FF)
            {
                return true; // Transporte e mapas
            }

            if (codePoint >= 0x1F700 && codePoint <= 0x1F77F)
            {
                return true; // Alquimicos
            }

            if (codePoint >= 0x1F780 && codePoint <= 0x1F7FF)
            {
                return true; // Geometricos estendidos
            }

            if (codePoint >= 0x1F800 && codePoint <= 0x1F8FF)
            {
                return true; // Setas suplementares
            }

            if (codePoint >= 0x1F900 && codePoint <= 0x1F9FF)
            {
                return true; // Simbolos e pictogramas suplementares
            }

            if (codePoint >= 0x1FA00 && codePoint <= 0x1FA6F)
            {
                return true; // Instrumentos de xadrez e outros
            }

            if (codePoint >= 0x1FA70 && codePoint <= 0x1FAFF)
            {
                return true; // Simbolos pictoricos estendidos-A
            }

            if (codePoint >= 0x2600 && codePoint <= 0x26FF)
            {
                return true;   // Simbolos diversos
            }

            if (codePoint >= 0x2700 && codePoint <= 0x27BF)
            {
                return true;   // Dingbats
            }
        }

        return false;
    }
}
