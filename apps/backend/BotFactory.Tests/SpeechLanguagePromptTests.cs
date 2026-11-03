using BotFactory.Core.Services;

namespace BotFactory.Tests;

public class SpeechLanguagePromptTests
{
    [Fact]
    public void LanguageCode_IsEs419()
    {
        Assert.Equal("es-419", SpeechLanguagePrompt.LanguageCode);
    }

    [Fact]
    public void BuildSystemInstruction_AppendsLatinAmericanSpanishSuffix()
    {
        var result = SpeechLanguagePrompt.BuildSystemInstruction("Eres un asistente.");

        Assert.StartsWith("Eres un asistente.", result);
        Assert.Contains("español (Latinoamérica)", result);
        Assert.Contains("formula siempre los argumentos en español", result);
        Assert.Contains("https://", result);
        Assert.Contains("usuario@dominio", result);
        Assert.Contains("1311", result);
        Assert.Contains("No deletrees", result);
        Assert.Contains("varias preguntas", result);
        Assert.EndsWith(SpeechLanguagePrompt.SystemInstructionSuffix, result);
    }

    [Fact]
    public void BuildSystemInstruction_TrimsTrailingWhitespaceFromMasterPrompt()
    {
        var result = SpeechLanguagePrompt.BuildSystemInstruction("Prompt  \n");

        Assert.Equal("Prompt" + SpeechLanguagePrompt.SystemInstructionSuffix, result);
    }
}
