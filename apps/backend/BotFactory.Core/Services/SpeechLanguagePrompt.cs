namespace BotFactory.Core.Services;

/// <summary>
/// Fixed Latin American Spanish locale for the voice pipeline (STT/TTS in agent, LLM here).
/// Platform-wide rules for how structured data must appear in reply text so chat,
/// the voice contact card, and TTS sanitization stay aligned.
/// </summary>
public static class SpeechLanguagePrompt
{
    public const string LanguageCode = "es-419";

    public const string SystemInstructionSuffix =
        "\n\nResponde siempre en español (Latinoamérica). Usa un español natural de Latinoamérica. "
        + "Cuando invoques herramientas (llamadas a funciones), formula siempre los argumentos en español. "
        + "Si la persona hace varias preguntas en el mismo mensaje, respóndelas todas de forma breve y ordenada. "
        + "Si cambia de tema, deja el tema anterior y enfócate en lo más reciente. "
        + "Como es una conversación por voz, sé breve y directo: da primero la información esencial "
        + "y evita rodeos innecesarios. "
        // Canonical text — chat / contact card can only show what you write.
        + "Datos estructurados (obligatorio en el texto de tu respuesta, forma canónica): "
        + "enlaces siempre como URL completa con https:// (ej. https://puravidacr.go.cr); "
        + "teléfonos y números de servicio con dígitos (ej. 800-000-0000 o 1311), nunca en palabras; "
        + "correos como usuario@dominio.go.cr. "
        + "Nunca digas que un enlace, teléfono o correo está «en pantalla» o «en el chat» sin escribirlo "
        + "en ese mismo mensaje. No uses Markdown ni asteriscos. "
        + "No deletrees dominios, correos ni números; no digas «punto», «arroba», nombres de letras "
        + "ni «guion» para deletrear. La plataforma se encarga de cómo se escuchan en voz. "
        + "Montos y precios: en palabras con la moneda (ej. veinticinco mil colones). "
        + "Fechas y horas: en forma natural hablada (ej. doce de agosto de dos mil veintiséis; "
        + "tres de la tarde).";

    public static string BuildSystemInstruction(string masterPrompt) =>
        masterPrompt.TrimEnd() + SystemInstructionSuffix;
}
