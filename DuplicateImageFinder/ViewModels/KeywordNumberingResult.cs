namespace ImageCollectionTool.ViewModels
{
    // Holds the numbering check result for a single keyword group in subfolder scan mode.
    public class KeywordNumberingResult
    {
        public string Keyword { get; }
        public string Text    { get; }
        public bool HasIssues { get; }

        public KeywordNumberingResult(string keyword, string text, bool hasIssues)
        {
            Keyword   = keyword;
            Text      = text;
            HasIssues = hasIssues;
        }
    }
}
