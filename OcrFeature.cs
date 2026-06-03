using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

internal enum OcrEngineKind
{
    Standard,
    Accurate
}

internal enum OcrTextLayout
{
    Lines,
    SmartParagraph
}

internal sealed class OcrResult
{
    public OcrResult(OcrEngineKind engine, string sourceScope, IList<string> lines)
    {
        Engine = engine;
        SourceScope = sourceScope ?? string.Empty;
        Lines = new List<string>(lines ?? new string[0]).AsReadOnly();
    }

    public OcrEngineKind Engine { get; private set; }
    public string SourceScope { get; private set; }
    public IList<string> Lines { get; private set; }
}

internal static class BaiduOcrClient
{
    private const string TokenUrl = "https://aip.baidubce.com/oauth/2.0/token";
    private const string StandardUrl = "https://aip.baidubce.com/rest/2.0/ocr/v1/general_basic";
    private const string AccurateUrl = "https://aip.baidubce.com/rest/2.0/ocr/v1/accurate_basic";
    private const int RequestTimeoutMilliseconds = 20000;
    private static readonly object TokenSync = new object();
    private static string cachedToken;
    private static string cachedTokenKey;
    private static DateTime cachedTokenExpiresUtc;

    public static OcrResult Recognize(byte[] pngBytes, AppSettings settings, string sourceScope)
    {
        if (pngBytes == null || pngBytes.Length == 0)
        {
            throw new InvalidOperationException(UiText.OcrEmptyImage);
        }
        if (settings == null || string.IsNullOrWhiteSpace(settings.BaiduOcrApiKey) || string.IsNullOrWhiteSpace(settings.BaiduOcrSecretKey))
        {
            throw new InvalidOperationException(UiText.OcrCredentialsMissing);
        }

        OcrEngineKind engine = NormalizeEngine(settings.OcrEngine);
        string token = GetAccessToken(settings.BaiduOcrApiKey, settings.BaiduOcrSecretKey);
        string url = (engine == OcrEngineKind.Accurate ? AccurateUrl : StandardUrl) + "?access_token=" + UrlEncode(token);
        string form = "image=" + UrlEncode(Convert.ToBase64String(pngBytes)) + "&detect_direction=true&paragraph=true";
        string json = PostForm(url, form);
        return new OcrResult(engine, sourceScope, ParseLines(json));
    }

    private static string GetAccessToken(string apiKey, string secretKey)
    {
        string cacheKey = apiKey + "\0" + secretKey;
        lock (TokenSync)
        {
            if (!string.IsNullOrEmpty(cachedToken) &&
                string.Equals(cachedTokenKey, cacheKey, StringComparison.Ordinal) &&
                cachedTokenExpiresUtc > DateTime.UtcNow.AddMinutes(2))
            {
                return cachedToken;
            }
        }

        string form = "grant_type=client_credentials&client_id=" + UrlEncode(apiKey) + "&client_secret=" + UrlEncode(secretKey);
        string json = PostForm(TokenUrl, form);
        Dictionary<string, object> root = ParseObject(json);
        object tokenValue;
        if (!root.TryGetValue("access_token", out tokenValue) || tokenValue == null || string.IsNullOrWhiteSpace(tokenValue.ToString()))
        {
            ThrowBaiduError(root, UiText.OcrTokenFailed);
        }

        int expires = 2592000;
        object expiresValue;
        if (root.TryGetValue("expires_in", out expiresValue))
        {
            int parsedExpires;
            if (int.TryParse(Convert.ToString(expiresValue, System.Globalization.CultureInfo.InvariantCulture), out parsedExpires))
            {
                expires = parsedExpires;
            }
        }
        string token = tokenValue.ToString();
        lock (TokenSync)
        {
            cachedToken = token;
            cachedTokenKey = cacheKey;
            cachedTokenExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, expires - 60));
        }
        return token;
    }

    private static IList<string> ParseLines(string json)
    {
        Dictionary<string, object> root = ParseObject(json);
        if (root.ContainsKey("error_code") || root.ContainsKey("error_msg"))
        {
            ThrowBaiduError(root, UiText.OcrRequestFailed);
        }

        object wordsResult;
        if (!root.TryGetValue("words_result", out wordsResult) || wordsResult == null)
        {
            return new string[0];
        }

        List<string> lines = new List<string>();
        object[] rows = wordsResult as object[];
        if (rows == null)
        {
            return lines;
        }

        for (int i = 0; i < rows.Length; i++)
        {
            Dictionary<string, object> row = rows[i] as Dictionary<string, object>;
            if (row == null)
            {
                continue;
            }
            object words;
            if (row.TryGetValue("words", out words) && words != null)
            {
                string text = words.ToString().Trim();
                if (text.Length > 0)
                {
                    lines.Add(text);
                }
            }
        }
        return lines;
    }

    private static string PostForm(string url, string form)
    {
        ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | (SecurityProtocolType)3072;
        byte[] body = Encoding.UTF8.GetBytes(form ?? string.Empty);
        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
        request.Method = "POST";
        request.Timeout = RequestTimeoutMilliseconds;
        request.ReadWriteTimeout = RequestTimeoutMilliseconds;
        request.ContentType = "application/x-www-form-urlencoded";
        request.Accept = "application/json";
        request.ContentLength = body.Length;
        using (Stream stream = request.GetRequestStream())
        {
            stream.Write(body, 0, body.Length);
        }

        try
        {
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }
        catch (WebException ex)
        {
            string text = ReadErrorResponse(ex);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
            throw;
        }
    }

    private static string ReadErrorResponse(WebException ex)
    {
        if (ex == null || ex.Response == null)
        {
            return null;
        }
        using (Stream stream = ex.Response.GetResponseStream())
        {
            if (stream == null)
            {
                return null;
            }
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }
    }

    private static Dictionary<string, object> ParseObject(string json)
    {
        object parsed = new JavaScriptSerializer().DeserializeObject(json ?? "{}");
        Dictionary<string, object> root = parsed as Dictionary<string, object>;
        if (root == null)
        {
            throw new InvalidOperationException(UiText.OcrResponseInvalid);
        }
        return root;
    }

    private static void ThrowBaiduError(Dictionary<string, object> root, string prefix)
    {
        object code;
        object message;
        root.TryGetValue("error_code", out code);
        root.TryGetValue("error_msg", out message);
        if (message == null)
        {
            root.TryGetValue("error_description", out message);
        }
        throw new InvalidOperationException(prefix + Environment.NewLine + Convert.ToString(code, System.Globalization.CultureInfo.InvariantCulture) + " " + Convert.ToString(message, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string UrlEncode(string value)
    {
        value = value ?? string.Empty;
        const int chunk = 24000;
        if (value.Length <= chunk)
        {
            return Uri.EscapeDataString(value);
        }

        StringBuilder builder = new StringBuilder(value.Length + value.Length / 5);
        for (int i = 0; i < value.Length; i += chunk)
        {
            int length = Math.Min(chunk, value.Length - i);
            builder.Append(Uri.EscapeDataString(value.Substring(i, length)));
        }
        return builder.ToString();
    }

    public static OcrEngineKind NormalizeEngine(OcrEngineKind engine)
    {
        return engine == OcrEngineKind.Accurate ? OcrEngineKind.Accurate : OcrEngineKind.Standard;
    }
}

internal static class GoogleTranslateClient
{
    private const string TranslateUrl = "https://translate.googleapis.com/translate_a/single";
    private const int RequestTimeoutMilliseconds = 20000;
    private const int MaxQueryChars = 1700;

    public static string TranslateToChinese(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        IList<string> chunks = SplitText(text.Trim(), MaxQueryChars);
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < chunks.Count; i++)
        {
            string translated = TranslateChunkToChinese(chunks[i]);
            if (string.IsNullOrWhiteSpace(translated))
            {
                continue;
            }
            if (builder.Length > 0)
            {
                builder.Append(Environment.NewLine);
            }
            builder.Append(translated.Trim());
        }
        return builder.ToString();
    }

    internal static string ParseTranslatedTextForSelfTest(string json)
    {
        return ParseTranslatedText(json);
    }

    private static string TranslateChunkToChinese(string text)
    {
        ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | (SecurityProtocolType)3072;
        string url = TranslateUrl +
            "?client=gtx&sl=auto&tl=zh-CN&dt=t&q=" +
            Uri.EscapeDataString(text ?? string.Empty);
        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
        request.Method = "GET";
        request.Timeout = RequestTimeoutMilliseconds;
        request.ReadWriteTimeout = RequestTimeoutMilliseconds;
        request.Accept = "application/json";

        try
        {
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                return ParseTranslatedText(reader.ReadToEnd());
            }
        }
        catch (WebException ex)
        {
            string textResponse = ReadErrorResponse(ex);
            if (!string.IsNullOrWhiteSpace(textResponse))
            {
                throw new InvalidOperationException(UiText.OcrTranslateFailed + Environment.NewLine + textResponse);
            }
            throw;
        }
    }

    private static string ParseTranslatedText(string json)
    {
        object parsed = new JavaScriptSerializer().DeserializeObject(json ?? "[]");
        object[] root = parsed as object[];
        if (root == null || root.Length == 0)
        {
            throw new InvalidOperationException(UiText.OcrTranslateResponseInvalid);
        }

        object[] segments = root[0] as object[];
        if (segments == null)
        {
            throw new InvalidOperationException(UiText.OcrTranslateResponseInvalid);
        }

        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < segments.Length; i++)
        {
            object[] segment = segments[i] as object[];
            if (segment != null && segment.Length > 0 && segment[0] != null)
            {
                builder.Append(segment[0].ToString());
            }
        }

        string translated = builder.ToString().Trim();
        if (translated.Length == 0)
        {
            throw new InvalidOperationException(UiText.OcrTranslateResponseInvalid);
        }
        return translated;
    }

    private static IList<string> SplitText(string text, int maxChars)
    {
        List<string> chunks = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return chunks;
        }

        int index = 0;
        while (index < text.Length)
        {
            int remaining = text.Length - index;
            if (remaining <= maxChars)
            {
                chunks.Add(text.Substring(index).Trim());
                break;
            }

            int length = FindSplitLength(text, index, maxChars);
            chunks.Add(text.Substring(index, length).Trim());
            index += length;
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }
        }
        return chunks;
    }

    private static int FindSplitLength(string text, int start, int maxChars)
    {
        int limit = Math.Min(text.Length, start + maxChars);
        for (int i = limit - 1; i > start + maxChars / 2; i--)
        {
            char value = text[i];
            if (value == '\n')
            {
                return i - start + 1;
            }
        }
        for (int i = limit - 1; i > start + maxChars / 2; i--)
        {
            if (IsSplitPunctuation(text[i]) || char.IsWhiteSpace(text[i]))
            {
                return i - start + 1;
            }
        }
        return maxChars;
    }

    private static bool IsSplitPunctuation(char value)
    {
        return value == '.' || value == '!' || value == '?' || value == ';' ||
            value == '\u3002' || value == '\uff01' || value == '\uff1f' || value == '\uff1b';
    }

    private static string ReadErrorResponse(WebException ex)
    {
        if (ex == null || ex.Response == null)
        {
            return null;
        }
        using (Stream stream = ex.Response.GetResponseStream())
        {
            if (stream == null)
            {
                return null;
            }
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }
    }
}

internal static class OcrTextFormatter
{
    public static OcrTextLayout NormalizeLayout(OcrTextLayout layout)
    {
        if (layout == OcrTextLayout.Lines)
        {
            return OcrTextLayout.Lines;
        }
        return OcrTextLayout.SmartParagraph;
    }

    public static string Format(IList<string> lines, OcrTextLayout layout)
    {
        List<string> clean = CleanLines(lines);
        if (clean.Count == 0)
        {
            return string.Empty;
        }

        layout = NormalizeLayout(layout);
        if (layout == OcrTextLayout.Lines)
        {
            return string.Join(Environment.NewLine, clean.ToArray());
        }
        return FormatSmartParagraph(clean);
    }

    private static List<string> CleanLines(IList<string> lines)
    {
        List<string> clean = new List<string>();
        if (lines == null)
        {
            return clean;
        }
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i] == null ? string.Empty : lines[i].Trim();
            if (line.Length > 0)
            {
                clean.Add(line);
            }
        }
        return clean;
    }

    private static string FormatSmartParagraph(List<string> lines)
    {
        StringBuilder output = new StringBuilder();
        StringBuilder paragraph = new StringBuilder();
        string previousLine = null;
        for (int i = 0; i < lines.Count; i++)
        {
            string line = NormalizeInlineSpaces(lines[i]);
            if (paragraph.Length > 0 && ShouldStartNewParagraph(previousLine, line))
            {
                AppendParagraph(output, paragraph);
                paragraph.Length = 0;
            }
            AppendJoined(paragraph, line);
            previousLine = line;
        }
        AppendParagraph(output, paragraph);
        return output.ToString();
    }

    private static string NormalizeInlineSpaces(string text)
    {
        StringBuilder builder = new StringBuilder();
        bool previousSpace = false;
        for (int i = 0; i < text.Length; i++)
        {
            char value = text[i];
            if (char.IsWhiteSpace(value))
            {
                if (!previousSpace)
                {
                    builder.Append(' ');
                    previousSpace = true;
                }
            }
            else
            {
                builder.Append(value);
                previousSpace = false;
            }
        }
        return builder.ToString();
    }

    private static void AppendParagraph(StringBuilder output, StringBuilder paragraph)
    {
        if (paragraph.Length == 0)
        {
            return;
        }
        if (output.Length > 0)
        {
            output.Append(Environment.NewLine);
            output.Append(Environment.NewLine);
        }
        output.Append(paragraph.ToString().Trim());
    }

    private static void AppendJoined(StringBuilder builder, string text)
    {
        if (builder.Length > 0 && builder[builder.Length - 1] == '-' && NeedsHyphenMerge(text[0]))
        {
            builder.Length = builder.Length - 1;
        }
        else if (builder.Length > 0 && NeedsSpace(builder[builder.Length - 1], text[0]))
        {
            builder.Append(' ');
        }
        builder.Append(text);
    }

    private static bool NeedsSpace(char previous, char next)
    {
        return IsAsciiWord(previous) && IsAsciiWord(next);
    }

    private static bool IsAsciiWord(char value)
    {
        return (value >= '0' && value <= '9') || (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z');
    }

    private static bool NeedsHyphenMerge(char next)
    {
        return IsAsciiWord(next);
    }

    private static bool ShouldStartNewParagraph(string previousLine, string line)
    {
        if (string.IsNullOrEmpty(previousLine) || string.IsNullOrEmpty(line))
        {
            return false;
        }
        if (LooksLikeListItem(line))
        {
            return true;
        }
        if (LooksLikeHeading(line) && EndsSentence(previousLine))
        {
            return true;
        }
        return EndsSentence(previousLine) && !StartsLikeContinuation(line);
    }

    private static bool StartsLikeContinuation(string line)
    {
        char first = FirstMeaningfulChar(line);
        if (first == '\0')
        {
            return true;
        }
        if (first == ',' || first == '\uff0c' || first == '\u3001' ||
            first == ')' || first == '\uff09' || first == ']' || first == '\uff3d' ||
            first == '}' || first == '\u300b' || first == '\u300d' || first == '\u201d')
        {
            return true;
        }
        return first >= 'a' && first <= 'z';
    }

    private static char FirstMeaningfulChar(string line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            char value = line[i];
            if (!char.IsWhiteSpace(value) && value != '"' && value != '\'' &&
                value != '\u201c' && value != '\u2018' && value != '\u300a' && value != '\u300c')
            {
                return value;
            }
        }
        return '\0';
    }

    private static bool LooksLikeHeading(string line)
    {
        if (EndsSentence(line) || LooksLikeListItem(line))
        {
            return false;
        }
        int count = CountTextCharacters(line);
        return count > 0 && count <= 18;
    }

    private static int CountTextCharacters(string line)
    {
        int count = 0;
        for (int i = 0; i < line.Length; i++)
        {
            if (!char.IsWhiteSpace(line[i]))
            {
                count++;
            }
        }
        return count;
    }

    private static bool LooksLikeListItem(string line)
    {
        string text = (line ?? string.Empty).TrimStart();
        if (text.Length < 2)
        {
            return false;
        }

        char first = text[0];
        if (first == '-' || first == '*' || first == '\u2022' || first == '\u00b7')
        {
            return char.IsWhiteSpace(text[1]) || text[1] == '\t';
        }

        int i = 0;
        while (i < text.Length && char.IsDigit(text[i]))
        {
            i++;
        }
        if (i > 0 && i <= 3 && i < text.Length && IsListSeparator(text[i]))
        {
            return true;
        }

        if (IsCjkListNumber(first) && text.Length > 1 && IsListSeparator(text[1]))
        {
            return true;
        }
        return false;
    }

    private static bool IsListSeparator(char value)
    {
        return value == '.' || value == '\u3001' || value == ')' || value == '\uff09';
    }

    private static bool IsCjkListNumber(char value)
    {
        return value == '\u4e00' || value == '\u4e8c' || value == '\u4e09' || value == '\u56db' ||
            value == '\u4e94' || value == '\u516d' || value == '\u4e03' || value == '\u516b' ||
            value == '\u4e5d' || value == '\u5341';
    }

    private static bool EndsSentence(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return false;
        }
        char value = line[line.Length - 1];
        return IsSentenceEnd(value);
    }

    private static bool IsSentenceEnd(char value)
    {
        return value == '.' || value == '!' || value == '?' || value == ';' ||
            value == '\u3002' || value == '\uff01' || value == '\uff1f' || value == '\uff1b';
    }
}
