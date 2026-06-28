// MainPage.Blog.cs
// Contains: GitHub Pages Blog Publisher workflows, static site generators, and event handlers.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using Windows.UI;

namespace JournalApp
{
    public sealed partial class MainPage
    {
        // ── Note Toolbar Toggle Handlers ─────────────────────────────────────

        private void PublishToBlogToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoadingNote || SelectedNote == null) return;
            SelectedNote.IsBlogPublished = true;
            JournalManager.Instance.SaveNotesMetadata();
            ShowStatusMessage("Marked for Blog Publication");
        }

        private void PublishToBlogToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isLoadingNote || SelectedNote == null) return;
            SelectedNote.IsBlogPublished = false;
            JournalManager.Instance.SaveNotesMetadata();
            ShowStatusMessage("Removed from Blog Publication");
        }

        // ── Settings Change Listeners ────────────────────────────────────────

        private void BlogRepoTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isPageInitialized) return;
            UpdateSaveSettingsButtonState();
        }

        private void BlogTitleTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isPageInitialized) return;
            UpdateSaveSettingsButtonState();
        }

        private void BlogDescTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isPageInitialized) return;
            UpdateSaveSettingsButtonState();
        }

        // ── Main Publishing Execution Flow ───────────────────────────────────

        private async void SettingsPublishBlogButton_Click(object sender, RoutedEventArgs e)
        {
            if (SettingsPublishBlogButton == null || BlogPublishStatusText == null || BlogPublishProgressRing == null) return;

            string token = GetSecureToken();
            if (string.IsNullOrEmpty(token)) token = GetSetting("GitHubToken");
            if (string.IsNullOrEmpty(token))
            {
                await ShowAlertAsync("Authentication Required", "Please connect your GitHub account in the Backup settings card first.");
                return;
            }

            var publishedNotes = JournalManager.Instance.Notes.Where(n => n.IsBlogPublished && !n.IsDeleted).ToList();
            if (publishedNotes.Count == 0)
            {
                await ShowAlertAsync("No Entries Selected", "You have not marked any journal entries for publication. Click the 'Blog' button on a note's toolbar to select it.");
                return;
            }

            // Confirm public warning if locked categories are present
            bool hasLockedNotes = publishedNotes.Any(n => _lockedCategories.Contains(n.Category));
            if (hasLockedNotes)
            {
                var confirmDialog = new ContentDialog
                {
                    Title = "Publish Encrypted Notes?",
                    Content = "Some selected entries belong to locked/encrypted categories. Publishing them will decrypt and host their content as public text on the web. Do you wish to continue?",
                    PrimaryButtonText = "Publish",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };
                var confirmRes = await confirmDialog.ShowAsync();
                if (confirmRes != ContentDialogResult.Primary) return;
            }

            // Start Publish UI state
            SettingsPublishBlogButton.IsEnabled = false;
            BlogPublishProgressRing.Visibility = Visibility.Visible;
            BlogPublishProgressRing.IsActive = true;
            BlogPublishStatusText.Text = "Initializing...";

            try
            {
                string username = await GetGitHubUsername(token);
                string repoName = BlogRepoTextBox?.Text?.Trim() ?? "my-journal-blog";
                repoName = System.Text.RegularExpressions.Regex.Replace(repoName, @"\s+", "-");
                repoName = System.Text.RegularExpressions.Regex.Replace(repoName, @"[^a-zA-Z0-9\-_\.]", "").ToLowerInvariant();

                string blogTitle = BlogTitleTextBox?.Text?.Trim() ?? "My Journal Blog";
                string blogDesc = BlogDescTextBox?.Text?.Trim() ?? "A collection of my thoughts and memories.";

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.TryParseAdd("JournalApp");
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                // 1. Verify or create repository
                BlogPublishStatusText.Text = "Verifying repository on GitHub...";
                var repoResponse = await client.GetAsync($"https://api.github.com/repos/{username}/{repoName}");
                if (repoResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    BlogPublishStatusText.Text = "Creating public repository...";
                    var repoCreatePayload = new
                    {
                        name = repoName,
                        description = "Static blog generated automatically by JournalApp",
                        @private = false,
                        auto_init = true
                    };
                    string createJson = JsonSerializer.Serialize(repoCreatePayload);
                    var createContent = new StringContent(createJson, System.Text.Encoding.UTF8, "application/json");
                    var createResponse = await client.PostAsync("https://api.github.com/user/repos", createContent);
                    if (!createResponse.IsSuccessStatusCode)
                    {
                        throw new Exception($"Failed to create repository: {createResponse.ReasonPhrase}");
                    }
                    // Wait briefly for GitHub to instantiate the repository and main branch
                    await Task.Delay(3000);
                }

                // 2. Ensure GitHub Pages is enabled
                BlogPublishStatusText.Text = "Enabling GitHub Pages...";
                int pagesRetry = 3;
                bool pagesEnabled = false;
                while (pagesRetry > 0 && !pagesEnabled)
                {
                    var pagesPayload = new { source = new { branch = "main", path = "/" } };
                    string pagesJson = JsonSerializer.Serialize(pagesPayload);
                    var pagesContent = new StringContent(pagesJson, System.Text.Encoding.UTF8, "application/json");
                    
                    // Accept header is required for pages API
                    client.DefaultRequestHeaders.Accept.Clear();
                    client.DefaultRequestHeaders.Accept.TryParseAdd("application/vnd.github+json");
                    
                    var pagesResponse = await client.PostAsync($"https://api.github.com/repos/{username}/{repoName}/pages", pagesContent);
                    if (pagesResponse.IsSuccessStatusCode || pagesResponse.StatusCode == System.Net.HttpStatusCode.Conflict)
                    {
                        pagesEnabled = true;
                    }
                    else
                    {
                        pagesRetry--;
                        await Task.Delay(3000);
                    }
                }

                // Restore default client Accept header
                client.DefaultRequestHeaders.Accept.Clear();

                // 3. Static Site Generation locally
                BlogPublishStatusText.Text = "Generating static blog site files...";
                string tempDir = Path.Combine(Path.GetTempPath(), "JournalBlog_" + Guid.NewGuid().ToString("N"));
                string tempPostsDir = Path.Combine(tempDir, "posts");
                Directory.CreateDirectory(tempDir);
                Directory.CreateDirectory(tempPostsDir);

                // Lists files that we will upload
                var filesToUpload = new List<(string LocalPath, string RemotePath)>();

                // A. Write premium stylesheet style.css
                string stylePath = Path.Combine(tempDir, "style.css");
                File.WriteAllText(stylePath, GetBlogStylesheetContent());
                filesToUpload.Add((stylePath, "style.css"));

                // B. Write individual posts and gather list details
                var blogPostItems = new List<BlogPostItem>();
                int currentNoteIndex = 1;

                foreach (var note in publishedNotes)
                {
                    BlogPublishStatusText.Text = $"Processing post {currentNoteIndex}/{publishedNotes.Count}...";
                    string plainText = GetNotePlainText(note);
                    
                    // Sync cover image if local
                    string githubImagePath = null;
                    if (!string.IsNullOrEmpty(note.HeroImagePath))
                    {
                        string localImgPath = JournalManager.Instance.GetAbsoluteMediaPath(note.HeroImagePath);
                        if (File.Exists(localImgPath))
                        {
                            string ext = Path.GetExtension(localImgPath);
                            string mediaFilename = $"cover_{note.Id}{ext}";
                            string remoteImgPath = $"media/{mediaFilename}";
                            
                            // Schedule cover image upload
                            filesToUpload.Add((localImgPath, remoteImgPath));
                            githubImagePath = $"media/{mediaFilename}";
                        }
                        else if (note.HeroImagePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                                 note.HeroImagePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            githubImagePath = note.HeroImagePath;
                        }
                    }

                    // Convert plain text to post HTML
                    string postHtml = GenerateBlogPostHtml(note, plainText, blogTitle, githubImagePath);
                    string postFilename = $"post_{note.Id}.html";
                    string localPostPath = Path.Combine(tempPostsDir, postFilename);
                    File.WriteAllText(localPostPath, postHtml);
                    filesToUpload.Add((localPostPath, $"posts/{postFilename}"));

                    // Get snippet
                    string snippet = string.IsNullOrWhiteSpace(note.Snippet) || note.Snippet == "No additional text"
                        ? (plainText.Length > 160 ? plainText.Substring(0, 160) + "..." : plainText)
                        : note.Snippet;

                    // Add to index catalog
                    blogPostItems.Add(new BlogPostItem
                    {
                        Id = note.Id,
                        Title = note.Title,
                        Category = note.Category,
                        CategoryColor = JournalManager.Instance.Categories.FirstOrDefault(c => c.Name.Equals(note.Category, StringComparison.OrdinalIgnoreCase))?.Color ?? "#808080",
                        DateCreated = note.DateCreated,
                        Snippet = snippet,
                        Mood = note.Mood,
                        Tags = note.Tags ?? new List<string>(),
                        CoverImage = githubImagePath,
                        LinkUrl = $"posts/{postFilename}"
                    });

                    currentNoteIndex++;
                }

                // C. Sort post catalog by creation date descending
                blogPostItems = blogPostItems.OrderByDescending(b => b.DateCreated).ToList();

                // D. Write index.html catalog main page
                string indexPath = Path.Combine(tempDir, "index.html");
                File.WriteAllText(indexPath, GenerateBlogIndexHtml(blogTitle, blogDesc, blogPostItems));
                filesToUpload.Add((indexPath, "index.html"));

                // 4. Upload files to GitHub
                int currentUploadIndex = 1;
                foreach (var file in filesToUpload)
                {
                    BlogPublishStatusText.Text = $"Uploading static asset {currentUploadIndex}/{filesToUpload.Count}: {Path.GetFileName(file.RemotePath)}...";
                    await SyncFileToGitHub(username, repoName, file.LocalPath, file.RemotePath, $"Publish static file {file.RemotePath} via JournalApp");
                    currentUploadIndex++;
                }

                // 5. Clean up local temp directory
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch {}

                // Done state
                BlogPublishStatusText.Text = "Successfully published to your live blog!";
                UpdateBlogLiveUrlLink(repoName);
                await ShowAlertAsync("Publication Complete", $"Your static site is successfully compiled and published to GitHub Pages!\n\nIt may take up to a minute for GitHub to configure and route the changes live.");
            }
            catch (Exception ex)
            {
                BlogPublishStatusText.Text = "Failed to publish blog";
                await ShowAlertAsync("Publish Failed", $"An error occurred during blog publication:\n{ex.Message}");
            }
            finally
            {
                SettingsPublishBlogButton.IsEnabled = true;
                BlogPublishProgressRing.IsActive = false;
                BlogPublishProgressRing.Visibility = Visibility.Collapsed;
            }
        }

        // ── Helper Code Conversions & HTML Generators ────────────────────────

        private string GetNotePlainText(JournalNote note)
        {
            try
            {
                string rtfPath = JournalManager.Instance.GetAbsoluteRtfPath(note.RtfFileName);
                if (!File.Exists(rtfPath)) return string.Empty;

                byte[] fileBytes = File.ReadAllBytes(rtfPath);
                bool isEncrypted = false;
                if (fileBytes.Length >= 5)
                {
                    if (!(fileBytes[0] == 123 && fileBytes[1] == 92 && fileBytes[2] == 114 && fileBytes[3] == 116 && fileBytes[4] == 102))
                    {
                        isEncrypted = true;
                    }
                }

                byte[] loadedBytes = fileBytes;
                if (isEncrypted)
                {
                    if (!string.IsNullOrEmpty(_masterPassword))
                    {
                        try
                        {
                            loadedBytes = EncryptionHelper.Decrypt(fileBytes, _masterPassword);
                        }
                        catch
                        {
                            return "[Decryption Failed - Check Master Password]";
                        }
                    }
                    else
                    {
                        return "[Encrypted Note - Unlock Settings First]";
                    }
                }

                string rtfText = System.Text.Encoding.UTF8.GetString(loadedBytes);
                
                // Headless RichEditBox to parse RTF to PlainText
                var tempBox = new Microsoft.UI.Xaml.Controls.RichEditBox();
                tempBox.Document.SetText(Microsoft.UI.Text.TextSetOptions.FormatRtf, rtfText);
                tempBox.Document.GetText(Microsoft.UI.Text.TextGetOptions.UseLf, out string plainText);
                return plainText?.Trim() ?? string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to parse note text: {ex.Message}");
                return string.Empty;
            }
        }

        private string GenerateBlogPostHtml(JournalNote note, string plainText, string blogTitle, string coverImagePath)
        {
            string title = string.IsNullOrWhiteSpace(note.Title) ? "Untitled Post" : note.Title.Trim();
            string dateString = note.DateCreated.ToString("MMMM d, yyyy");
            string category = note.Category;
            var catInfo = JournalManager.Instance.Categories.FirstOrDefault(c => c.Name.Equals(category, StringComparison.OrdinalIgnoreCase));
            string badgeColor = catInfo?.Color ?? "#808080";

            // Mood badge
            string moodBadgeHtml = "";
            if (!string.IsNullOrEmpty(note.Mood) && note.Mood != "None")
            {
                moodBadgeHtml = $"<span class=\"mood-badge\">{System.Net.WebUtility.HtmlEncode(note.Mood)}</span>";
            }

            // Tag badges
            string tagsHtml = "";
            if (note.Tags != null && note.Tags.Count > 0)
            {
                foreach (var tag in note.Tags)
                {
                    if (!string.IsNullOrWhiteSpace(tag))
                    {
                        tagsHtml += $"<span class=\"tag-badge\">#{System.Net.WebUtility.HtmlEncode(tag.TrimStart('#'))}</span>";
                    }
                }
            }

            // Cover Image banner
            string coverHtml = "";
            if (!string.IsNullOrEmpty(coverImagePath))
            {
                coverHtml = $"<div class=\"cover-container\"><img class=\"cover-img\" src=\"../{coverImagePath}\" alt=\"Cover banner\"></div>";
            }

            // Parse text paragraphs / lists / headers
            string[] rawLines = plainText.Split(new[] { "\n", "\r" }, StringSplitOptions.None);
            string bodyHtml = "";
            bool inPre = false;
            bool inUl = false;
            bool inBlockquote = false;

            foreach (var line in rawLines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("```"))
                {
                    if (inUl) { bodyHtml += "</ul>"; inUl = false; }
                    if (inBlockquote) { bodyHtml += "</blockquote>"; inBlockquote = false; }

                    if (inPre) { bodyHtml += "</pre>"; inPre = false; }
                    else { bodyHtml += "<pre>"; inPre = true; }
                    continue;
                }

                if (inPre)
                {
                    bodyHtml += System.Net.WebUtility.HtmlEncode(line) + "\n";
                    continue;
                }

                if (trimmed.StartsWith(">"))
                {
                    if (inUl) { bodyHtml += "</ul>"; inUl = false; }
                    string quoteContent = trimmed.Substring(1).TrimStart();
                    string parsedContent = ProcessInlineMarkdown(System.Net.WebUtility.HtmlEncode(quoteContent));

                    if (!inBlockquote)
                    {
                        bodyHtml += "<blockquote>";
                        inBlockquote = true;
                    }
                    bodyHtml += $"<p>{parsedContent}</p>";
                    continue;
                }
                else if (inBlockquote)
                {
                    bodyHtml += "</blockquote>";
                    inBlockquote = false;
                }

                if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                {
                    string itemContent = trimmed.Substring(2);
                    string parsedContent = ProcessInlineMarkdown(System.Net.WebUtility.HtmlEncode(itemContent));

                    if (!inUl)
                    {
                        bodyHtml += "<ul>";
                        inUl = true;
                    }
                    bodyHtml += $"<li>{parsedContent}</li>";
                    continue;
                }
                else if (inUl)
                {
                    bodyHtml += "</ul>";
                    inUl = false;
                }

                if (trimmed.StartsWith("# "))
                {
                    bodyHtml += $"<h1>{ProcessInlineMarkdown(System.Net.WebUtility.HtmlEncode(trimmed.Substring(2)))}</h1>";
                }
                else if (trimmed.StartsWith("## "))
                {
                    bodyHtml += $"<h2>{ProcessInlineMarkdown(System.Net.WebUtility.HtmlEncode(trimmed.Substring(3)))}</h2>";
                }
                else if (trimmed.StartsWith("### "))
                {
                    bodyHtml += $"<h3>{ProcessInlineMarkdown(System.Net.WebUtility.HtmlEncode(trimmed.Substring(4)))}</h3>";
                }
                else if (trimmed == "---")
                {
                    bodyHtml += "<hr>";
                }
                else if (string.IsNullOrWhiteSpace(trimmed))
                {
                    bodyHtml += "<br>";
                }
                else
                {
                    bodyHtml += $"<p>{ProcessInlineMarkdown(System.Net.WebUtility.HtmlEncode(line))}</p>";
                }
            }

            if (inPre) bodyHtml += "</pre>";
            if (inUl) bodyHtml += "</ul>";
            if (inBlockquote) bodyHtml += "</blockquote>";

            return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{System.Net.WebUtility.HtmlEncode(title)} - {System.Net.WebUtility.HtmlEncode(blogTitle)}</title>
    <link rel=""preconnect"" href=""https://fonts.googleapis.com"">
    <link rel=""preconnect"" href=""https://fonts.gstatic.com"" crossorigin>
    <link href=""https://fonts.googleapis.com/css2?family=Playfair+Display:ital,wght@0,400..900;1,400..900&family=Plus+Jakarta+Sans:ital,wght@0,200..800;1,200..800&display=swap"" rel=""stylesheet"">
    <link rel=""stylesheet"" href=""../style.css"">
</head>
<body>
    <div class=""nav-back"">
        <a href=""../index.html"">← Back to Blog</a>
    </div>

    <article class=""post-detail"">
        {coverHtml}
        
        <header class=""post-header"">
            <h1 class=""post-title"">{System.Net.WebUtility.HtmlEncode(title)}</h1>
            <div class=""post-meta-container"">
                <span class=""post-date"">{dateString}</span>
                <span class=""category-badge"" style=""background-color: {badgeColor}12; color: {badgeColor}; border: 1px solid {badgeColor}25;"">{System.Net.WebUtility.HtmlEncode(category)}</span>
                {moodBadgeHtml}
                <div class=""tags-list"">{tagsHtml}</div>
            </div>
        </header>

        <section class=""post-content"">
            {bodyHtml}
        </section>
    </article>

    <footer>
        <p>&copy; {DateTime.Now.Year} {System.Net.WebUtility.HtmlEncode(blogTitle)}. Generated via JournalApp.</p>
    </footer>
</body>
</html>";
        }

        private string GenerateBlogIndexHtml(string blogTitle, string blogDesc, List<BlogPostItem> posts)
        {
            string postsHtml = "";
            if (posts.Count == 0)
            {
                postsHtml = "<div class=\"no-posts\">No entries have been published to the blog yet.</div>";
            }
            else
            {
                foreach (var post in posts)
                {
                    string coverSnippet = "";
                    if (!string.IsNullOrEmpty(post.CoverImage))
                    {
                        coverSnippet = $"<div class=\"post-card-cover\"><img src=\"{post.CoverImage}\" alt=\"Post cover\"></div>";
                    }

                    string moodHtml = "";
                    if (!string.IsNullOrEmpty(post.Mood) && post.Mood != "None")
                    {
                        moodHtml = $"<span class=\"mood-badge\">{System.Net.WebUtility.HtmlEncode(post.Mood)}</span>";
                    }

                    string tagsHtml = "";
                    if (post.Tags != null && post.Tags.Count > 0)
                    {
                        foreach (var tag in post.Tags)
                        {
                            tagsHtml += $"<span class=\"tag-badge\">#{System.Net.WebUtility.HtmlEncode(tag.TrimStart('#'))}</span>";
                        }
                    }

                    postsHtml += $@"
            <article class=""post-card"">
                {coverSnippet}
                <div class=""post-card-content"">
                    <div class=""post-card-meta"">
                        <span class=""post-card-date"">{post.DateCreated.ToString("MMMM d, yyyy")}</span>
                        <span class=""category-badge"" style=""background-color: {post.CategoryColor}12; color: {post.CategoryColor}; border: 1px solid {post.CategoryColor}25;"">{System.Net.WebUtility.HtmlEncode(post.Category)}</span>
                        {moodHtml}
                    </div>
                    <h2 class=""post-card-title""><a href=""{post.LinkUrl}"">{System.Net.WebUtility.HtmlEncode(post.Title)}</a></h2>
                    <p class=""post-card-snippet"">{System.Net.WebUtility.HtmlEncode(post.Snippet)}</p>
                    <div class=""post-card-tags"">{tagsHtml}</div>
                </div>
            </article>";
                }
            }

            return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{System.Net.WebUtility.HtmlEncode(blogTitle)}</title>
    <link rel=""preconnect"" href=""https://fonts.googleapis.com"">
    <link rel=""preconnect"" href=""https://fonts.gstatic.com"" crossorigin>
    <link href=""https://fonts.googleapis.com/css2?family=Playfair+Display:ital,wght@0,400..900;1,400..900&family=Plus+Jakarta+Sans:ital,wght@0,200..800;1,200..800&display=swap"" rel=""stylesheet"">
    <link rel=""stylesheet"" href=""style.css"">
</head>
<body>
    <header class=""blog-header"">
        <div class=""header-container"">
            <h1 class=""blog-title"">{System.Net.WebUtility.HtmlEncode(blogTitle)}</h1>
            <p class=""blog-description"">{System.Net.WebUtility.HtmlEncode(blogDesc)}</p>
        </div>
    </header>

    <main class=""blog-container"">
        <div class=""posts-grid"">
            {postsHtml}
        </div>
    </main>

    <footer>
        <p>&copy; {DateTime.Now.Year} {System.Net.WebUtility.HtmlEncode(blogTitle)}. Generated via JournalApp.</p>
    </footer>
</body>
</html>";
        }

        private string GetBlogStylesheetContent()
        {
            return @"/* premium blog styles */
:root {
    --bg-color: #f7fafc;
    --card-bg: #ffffff;
    --text-primary: #1a202c;
    --text-secondary: #4a5568;
    --text-muted: #718096;
    --border-color: #e2e8f0;
    --font-sans: 'Plus Jakarta Sans', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
    --font-serif: 'Playfair Display', Georgia, Cambria, serif;
    --accent-color: #0078D4;
}

@media (prefers-color-scheme: dark) {
    :root {
        --bg-color: #0f172a;
        --card-bg: #1e293b;
        --text-primary: #f8fafc;
        --text-secondary: #cbd5e1;
        --text-muted: #94a3b8;
        --border-color: #334155;
    }
}

* {
    box-sizing: border-box;
    margin: 0;
    padding: 0;
}

body {
    background-color: var(--bg-color);
    color: var(--text-primary);
    font-family: var(--font-sans);
    line-height: 1.8;
    display: flex;
    flex-direction: column;
    min-height: 100vh;
}

.blog-header {
    background: linear-gradient(135deg, rgba(0,120,212,0.05) 0%, rgba(227,0,140,0.05) 100%);
    border-bottom: 1px solid var(--border-color);
    padding: 60px 20px;
    text-align: center;
}

.header-container {
    max-width: 800px;
    margin: 0 auto;
}

.blog-title {
    font-family: var(--font-serif);
    font-size: 3rem;
    font-weight: 800;
    letter-spacing: -0.03em;
    margin-bottom: 12px;
    background: linear-gradient(to right, #0078D4, #E3008C);
    -webkit-background-clip: text;
    -webkit-text-fill-color: transparent;
}

.blog-description {
    color: var(--text-secondary);
    font-size: 1.15rem;
    font-weight: 500;
}

.blog-container {
    max-width: 900px;
    margin: 40px auto;
    padding: 0 20px;
    flex-grow: 1;
    width: 100%;
}

.posts-grid {
    display: flex;
    flex-direction: column;
    gap: 32px;
}

.post-card {
    background-color: var(--card-bg);
    border: 1px solid var(--border-color);
    border-radius: 16px;
    overflow: hidden;
    display: flex;
    flex-direction: column;
    transition: transform 0.2s ease, box-shadow 0.2s ease;
    box-shadow: 0 4px 6px -1px rgba(0,0,0,0.05), 0 2px 4px -1px rgba(0,0,0,0.03);
}

@media (min-width: 640px) {
    .post-card {
        flex-direction: row;
    }
    .post-card-cover {
        width: 35%;
        min-width: 240px;
        flex-shrink: 0;
    }
}

.post-card:hover {
    transform: translateY(-2px);
    box-shadow: 0 10px 15px -3px rgba(0,0,0,0.08);
}

.post-card-cover {
    position: relative;
    overflow: hidden;
    background-color: var(--border-color);
    min-height: 180px;
}

.post-card-cover img {
    width: 100%;
    height: 100%;
    object-fit: cover;
    position: absolute;
    top: 0;
    left: 0;
}

.post-card-content {
    padding: 24px;
    display: flex;
    flex-direction: column;
    flex-grow: 1;
}

.post-card-meta {
    display: flex;
    align-items: center;
    gap: 12px;
    margin-bottom: 12px;
    font-size: 0.8rem;
}

.post-card-date {
    color: var(--text-muted);
    font-weight: 500;
}

.category-badge {
    display: inline-block;
    padding: 2px 10px;
    border-radius: 12px;
    font-size: 0.75rem;
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 0.05em;
}

.mood-badge {
    display: inline-block;
    background-color: rgba(245, 158, 11, 0.1);
    color: #d97706;
    border: 1px solid rgba(245, 158, 11, 0.25);
    padding: 2px 10px;
    border-radius: 12px;
    font-size: 0.75rem;
    font-weight: 600;
}

.post-card-title {
    font-family: var(--font-serif);
    font-size: 1.6rem;
    line-height: 1.3;
    margin-bottom: 8px;
}

.post-card-title a {
    color: var(--text-primary);
    text-decoration: none;
    transition: color 0.15s ease;
}

.post-card-title a:hover {
    color: #0078D4;
}

.post-card-snippet {
    color: var(--text-secondary);
    font-size: 0.95rem;
    margin-bottom: 16px;
    display: -webkit-box;
    -webkit-line-clamp: 2;
    -webkit-box-orient: vertical;
    overflow: hidden;
}

.post-card-tags, .tags-list {
    display: flex;
    flex-wrap: wrap;
    gap: 6px;
}

.tag-badge {
    display: inline-block;
    background-color: rgba(74, 85, 104, 0.06);
    color: var(--text-secondary);
    border: 1px solid var(--border-color);
    padding: 2px 8px;
    border-radius: 10px;
    font-size: 0.75rem;
    font-weight: 500;
}

/* Post Detail styling */
.nav-back {
    max-width: 760px;
    margin: 40px auto 0 auto;
    padding: 0 20px;
}

.nav-back a {
    color: var(--text-muted);
    text-decoration: none;
    font-size: 0.9rem;
    font-weight: 500;
    transition: color 0.15s ease;
}

.nav-back a:hover {
    color: var(--text-primary);
}

.post-detail {
    max-width: 760px;
    margin: 20px auto 40px auto;
    padding: 0 20px;
    background-color: var(--card-bg);
    border: 1px solid var(--border-color);
    border-radius: 16px;
    overflow: hidden;
    box-shadow: 0 4px 6px -1px rgba(0,0,0,0.05);
}

.cover-container {
    width: 100%;
    height: 320px;
    overflow: hidden;
    background-color: var(--border-color);
}

.cover-img {
    width: 100%;
    height: 100%;
    object-fit: cover;
}

.post-header {
    padding: 40px 40px 20px 40px;
    border-bottom: 1px solid var(--border-color);
}

.post-title {
    font-family: var(--font-serif);
    font-size: 2.8rem;
    line-height: 1.2;
    margin-bottom: 16px;
    font-weight: 700;
}

.post-meta-container {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    gap: 12px;
    font-size: 0.85rem;
}

.post-date {
    color: var(--text-muted);
    font-weight: 500;
}

.post-content {
    padding: 40px;
    font-size: 1.1rem;
    line-height: 1.8;
}

.post-content p {
    margin-bottom: 24px;
}

.post-content h1, .post-content h2, .post-content h3 {
    font-family: var(--font-serif);
    margin: 32px 0 16px 0;
    line-height: 1.3;
}

.post-content h1 { font-size: 2rem; }
.post-content h2 { font-size: 1.6rem; }
.post-content h3 { font-size: 1.3rem; }

.post-content blockquote {
    border-left: 4px solid #0078D4;
    padding-left: 20px;
    margin: 24px 0;
    font-style: italic;
    color: var(--text-secondary);
}

.post-content ul, .post-content ol {
    margin: 0 0 24px 24px;
}

.post-content li {
    margin-bottom: 8px;
}

.post-content pre {
    background-color: var(--bg-color);
    border: 1px solid var(--border-color);
    padding: 16px;
    border-radius: 8px;
    overflow-x: auto;
    font-family: monospace;
    font-size: 0.9rem;
    margin-bottom: 24px;
}

.post-content hr {
    border: 0;
    height: 1px;
    background-color: var(--border-color);
    margin: 40px 0;
}

/* Footer style */
footer {
    border-top: 1px solid var(--border-color);
    padding: 30px 20px;
    text-align: center;
    color: var(--text-muted);
    font-size: 0.85rem;
    margin-top: auto;
}

.no-posts {
    text-align: center;
    padding: 60px;
    color: var(--text-muted);
    font-size: 1.1rem;
    border: 2px dashed var(--border-color);
    border-radius: 12px;
    background-color: var(--card-bg);
}
";
        }

        private class BlogPostItem
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public string Category { get; set; }
            public string CategoryColor { get; set; }
            public DateTime DateCreated { get; set; }
            public string Snippet { get; set; }
            public string Mood { get; set; }
            public List<string> Tags { get; set; }
            public string CoverImage { get; set; }
            public string LinkUrl { get; set; }
        }
        private async void UpdateBlogLiveUrlLink(string blogRepo)
        {
            if (BlogLiveUrlButton == null) return;
            
            string token = GetSecureToken();
            if (string.IsNullOrEmpty(token)) token = GetSetting("GitHubToken");
            
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(blogRepo))
            {
                BlogLiveUrlButton.Content = "Not Configured (Connect GitHub first)";
                BlogLiveUrlButton.NavigateUri = null;
                return;
            }

            try
            {
                string username = await GetGitHubUsername(token);
                if (!string.IsNullOrEmpty(username))
                {
                    string url = $"https://{username}.github.io/{blogRepo}/";
                    BlogLiveUrlButton.Content = url;
                    BlogLiveUrlButton.NavigateUri = new Uri(url);
                }
                else
                {
                    BlogLiveUrlButton.Content = "Unable to fetch username";
                    BlogLiveUrlButton.NavigateUri = null;
                }
            }
            catch (Exception ex)
            {
                BlogLiveUrlButton.Content = $"Error: {ex.Message}";
                BlogLiveUrlButton.NavigateUri = null;
            }
        }
    }
}
