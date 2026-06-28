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

        private void BlogCustomCssTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isPageInitialized) return;
            UpdateSaveSettingsButtonState();
        }

        // ── Main Publishing Execution Flow ───────────────────────────────────

        private async void SettingsPublishBlogButton_Click(object sender, RoutedEventArgs e)
        {
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
            SetPublishingUiState(true, "Initializing...");

            try
            {
                string username = await GetGitHubUsername(token);
                if (string.IsNullOrEmpty(username))
                {
                    throw new Exception("Unable to retrieve your GitHub username. Please check your internet connection and token permissions.");
                }

                // Retrieve repository name
                string repoName = GetSetting("BlogRepo", "my-journal-blog");
                if (string.IsNullOrWhiteSpace(repoName)) repoName = "my-journal-blog";

                // Sanitise repo name for safety
                repoName = System.Text.RegularExpressions.Regex.Replace(repoName, @"\s+", "-");
                repoName = System.Text.RegularExpressions.Regex.Replace(repoName, @"[^a-zA-Z0-9\-_\.]", "").ToLowerInvariant();

                string blogTitle = BlogTitleTextBox?.Text?.Trim() ?? "My Journal Blog";
                string blogDesc = BlogDescTextBox?.Text?.Trim() ?? "A collection of my thoughts and memories.";

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.TryParseAdd("JournalApp");
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                // 1. Verify or create repository
                SetPublishingUiState(true, "Verifying repository on GitHub...");
                var repoResponse = await client.GetAsync($"https://api.github.com/repos/{username}/{repoName}");
                if (repoResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    SetPublishingUiState(true, "Creating public repository...");
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
                SetPublishingUiState(true, "Enabling GitHub Pages...");
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
                SetPublishingUiState(true, "Generating static blog site files...");
                string tempDir = Path.Combine(Path.GetTempPath(), "JournalBlog_" + Guid.NewGuid().ToString("N"));
                string tempPostsDir = Path.Combine(tempDir, "posts");
                Directory.CreateDirectory(tempDir);
                Directory.CreateDirectory(tempPostsDir);

                // Lists files that we will upload
                var filesToUpload = new List<(string LocalPath, string RemotePath)>();

                // A. Write premium stylesheet style.css
                string stylePath = Path.Combine(tempDir, "style.css");
                string customCss = GetSetting("BlogCustomCss", "");
                File.WriteAllText(stylePath, GetBlogStylesheetContent() + "\n" + customCss);
                filesToUpload.Add((stylePath, "style.css"));

                // B. Write individual posts and gather list details
                var blogPostItems = new List<BlogPostItem>();
                int currentNoteIndex = 1;

                foreach (var note in publishedNotes)
                {
                    SetPublishingUiState(true, $"Processing post {currentNoteIndex}/{publishedNotes.Count}...");
                    string plainText = GetNotePlainText(note);
                    
                    // Sync cover image if local or use Picsum fallback
                    string githubImagePath = null;
                    if (note.HeroImagePath != "None")
                    {
                        if (string.IsNullOrEmpty(note.HeroImagePath))
                        {
                            githubImagePath = $"https://picsum.photos/seed/{note.Id}/1200/600";
                        }
                        else
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
                    }

                    // Sync attached photos if local
                    var blogPhotoUrls = new List<string>();
                    if (note.AttachedPhotoPaths != null)
                    {
                        foreach (var photoPath in note.AttachedPhotoPaths)
                        {
                            if (string.IsNullOrEmpty(photoPath)) continue;
                            string localPhotoPath = JournalManager.Instance.GetAbsoluteMediaPath(photoPath);
                            if (File.Exists(localPhotoPath))
                            {
                                string cleanName = Path.GetFileName(photoPath);
                                string remotePhotoPath = $"media/{cleanName}";
                                filesToUpload.Add((localPhotoPath, remotePhotoPath));
                                blogPhotoUrls.Add($"../media/{cleanName}");
                            }
                        }
                    }

                    // Convert plain text to post HTML
                    string postHtml = GenerateBlogPostHtml(note, plainText, blogTitle, githubImagePath, blogPhotoUrls);
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
                    SetPublishingUiState(true, $"Uploading static asset {currentUploadIndex}/{filesToUpload.Count}: {Path.GetFileName(file.RemotePath)}...");
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
                UpdateBlogLiveUrlLink(repoName);
                await ShowAlertAsync("Publication Complete", $"Your static site is successfully compiled and published to GitHub Pages!\n\nIt may take up to a minute for GitHub to configure and route the changes live.");
            }
            catch (Exception ex)
            {
                await ShowAlertAsync("Publish Failed", $"An error occurred during blog publication:\n{ex.Message}");
            }
            finally
            {
                SetPublishingUiState(false);
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

        private string GenerateBlogPostHtml(JournalNote note, string plainText, string blogTitle, string coverImagePath, List<string> blogPhotoUrls)
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
                string src = (coverImagePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                              coverImagePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                             ? coverImagePath
                             : $"../{coverImagePath}";
                coverHtml = $"<div class=\"cover-container\"><img class=\"cover-img\" src=\"{src}\" alt=\"Cover banner\"></div>";
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

            string galleryHtml = "";
            if (blogPhotoUrls != null && blogPhotoUrls.Count > 0)
            {
                galleryHtml = "<div class=\"post-gallery\"><h3>Attached Photos</h3><div class=\"gallery-grid\">";
                foreach (var photoUrl in blogPhotoUrls)
                {
                    galleryHtml += $"<a href=\"javascript:void(0)\" onclick=\"openLightbox('{photoUrl}')\"><img src=\"{photoUrl}\" alt=\"Attached photo\"></a>";
                }
                galleryHtml += "</div></div>";
            }

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
        <button class=""share-btn"" onclick=""copyShareLink()"" style=""margin-left: 20px; background: none; border: none; color: var(--text-muted); cursor: pointer; font-size: 0.9rem;"">🔗 Share</button>
    </div>

    <article class=""post-detail"">
        {coverHtml}
        
        <header class=""post-header"">
            <h1 class=""post-title-detail"">{System.Net.WebUtility.HtmlEncode(title)}</h1>
            <div class=""post-meta"">
                <span class=""post-date"">{dateString}</span>
                <span class=""post-divider"">•</span>
                <span class=""post-category"" style=""color: {badgeColor}; font-weight: 600;"">{System.Net.WebUtility.HtmlEncode(category)}</span>
                {(!string.IsNullOrEmpty(note.Mood) && note.Mood != "None" ? $" <span class=\"post-divider\">•</span> <span class=\"post-mood\">{System.Net.WebUtility.HtmlEncode(note.Mood)}</span>" : "")}
            </div>
        </header>

        <section class=""post-content"">
            {bodyHtml}
            {galleryHtml}
        </section>
    </article>

    <footer>
        <p>&copy; {DateTime.Now.Year} {System.Net.WebUtility.HtmlEncode(blogTitle)}. Generated via JournalApp.</p>
    </footer>

    <!-- Overlay Lightbox Container -->
    <div id=""lightbox"" onclick=""closeLightbox()"" style=""display: none; position: fixed; z-index: 9999; top: 0; left: 0; width: 100%; height: 100%; background: rgba(0,0,0,0.85); align-items: center; justify-content: center; opacity: 0; transition: opacity 0.25s ease; cursor: pointer;"">
        <img id=""lightbox-img"" src="""" style=""max-width: 90%; max-height: 90%; object-fit: contain; border-radius: 4px; box-shadow: 0 8px 32px rgba(0,0,0,0.5); transform: scale(0.95); transition: transform 0.25s ease;"" alt=""Enlarged photo"">
    </div>

    <script>
        function copyShareLink() {{
            navigator.clipboard.writeText(window.location.href);
            alert('Link copied to clipboard!');
        }}
        function openLightbox(url) {{
            const lightbox = document.getElementById('lightbox');
            const img = document.getElementById('lightbox-img');
            img.src = url;
            lightbox.style.display = 'flex';
            setTimeout(() => {{
                lightbox.style.opacity = '1';
                img.style.transform = 'scale(1)';
            }}, 10);
        }}
        function closeLightbox() {{
            const lightbox = document.getElementById('lightbox');
            const img = document.getElementById('lightbox-img');
            lightbox.style.opacity = '0';
            img.style.transform = 'scale(0.95)';
            setTimeout(() => {{
                lightbox.style.display = 'none';
            }}, 250);
        }}
    </script>
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
                    string moodHtml = "";
                    if (!string.IsNullOrEmpty(post.Mood) && post.Mood != "None")
                    {
                        moodHtml = $" <span class=\"post-divider\">•</span> <span class=\"post-mood\">{System.Net.WebUtility.HtmlEncode(post.Mood)}</span>";
                    }

                    postsHtml += $@"
            <article class=""post-item"" onclick=""location.href='{post.LinkUrl}'"" style=""cursor: pointer;"">
                <div class=""post-meta"">
                    <span class=""post-date"">{post.DateCreated.ToString("MMMM d, yyyy")}</span>
                    <span class=""post-divider"">•</span>
                    <span class=""post-category"" style=""color: {post.CategoryColor};"">{System.Net.WebUtility.HtmlEncode(post.Category)}</span>
                    {moodHtml}
                </div>
                <h2 class=""post-title""><a href=""{post.LinkUrl}"">{System.Net.WebUtility.HtmlEncode(post.Title)}</a></h2>
                <p class=""post-snippet"">{System.Net.WebUtility.HtmlEncode(post.Snippet)}</p>
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
        <h1 class=""blog-title"">{System.Net.WebUtility.HtmlEncode(blogTitle)}</h1>
        <p class=""blog-description"">{System.Net.WebUtility.HtmlEncode(blogDesc)}</p>
    </header>

    <main class=""blog-container"">
        <div class=""posts-list"">
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
            return @"/* premium simple blog styles (Medium-like) */
:root {
    --bg-color: #ffffff;
    --text-primary: #292929;
    --text-secondary: #757575;
    --text-muted: #9e9e9e;
    --border-color: #f2f2f2;
    --link-color: #1a1a1a;
    --font-sans: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, Arial, sans-serif;
    --font-serif: Charter, Georgia, Cambria, ""Times New Roman"", Times, serif;
}

@media (prefers-color-scheme: dark) {
    :root {
        --bg-color: #121212;
        --text-primary: #e6e6e6;
        --text-secondary: #a0a0a0;
        --text-muted: #707070;
        --border-color: #242424;
        --link-color: #ffffff;
    }
}

body {
    background-color: var(--bg-color);
    color: var(--text-primary);
    font-family: var(--font-sans);
    line-height: 1.62;
    max-width: 680px;
    margin: 0 auto;
    padding: 80px 20px;
    -webkit-font-smoothing: antialiased;
}

.blog-header {
    margin-bottom: 50px;
    border-bottom: 1px solid var(--border-color);
    padding-bottom: 30px;
}

.blog-title {
    font-family: var(--font-serif);
    font-size: 2.5rem;
    font-weight: 700;
    margin-bottom: 8px;
    letter-spacing: -0.02em;
    color: var(--text-primary);
}

.blog-description {
    color: var(--text-secondary);
    font-size: 1.1rem;
    font-family: var(--font-sans);
}

.posts-list {
    display: flex;
    flex-direction: column;
    gap: 40px;
}

.post-item {
    border-bottom: 1px solid var(--border-color);
    padding-bottom: 40px;
    transition: transform 0.2s ease, opacity 0.2s ease;
}

.post-item:hover {
    opacity: 0.85;
}

.post-item:last-child {
    border-bottom: none;
}

.post-meta {
    font-size: 0.85rem;
    color: var(--text-secondary);
    margin-bottom: 10px;
    display: flex;
    align-items: center;
    gap: 8px;
}

.post-divider {
    color: var(--text-muted);
}

.post-title {
    font-family: var(--font-serif);
    font-size: 1.5rem;
    font-weight: 700;
    line-height: 1.25;
    margin-bottom: 10px;
    letter-spacing: -0.01em;
}

.post-title a {
    color: var(--link-color);
    text-decoration: none;
}

.post-title a:hover {
    color: var(--text-primary);
}

.post-snippet {
    color: var(--text-secondary);
    font-size: 0.95rem;
    line-height: 1.5;
}

/* Detail page styling */
.cover-container {
    width: 100%;
    margin-bottom: 35px;
    border-radius: 8px;
    overflow: hidden;
}

.cover-img {
    width: 100%;
    height: 380px;
    object-fit: cover;
    display: block;
}

.nav-back {
    margin-bottom: 40px;
    display: flex;
    align-items: center;
}

.nav-back a {
    color: var(--text-secondary);
    text-decoration: none;
    font-size: 0.9rem;
}

.nav-back a:hover {
    color: var(--text-primary);
}

.post-detail {
    margin-bottom: 60px;
}

.post-header {
    margin-bottom: 35px;
    border-bottom: 1px solid var(--border-color);
    padding-bottom: 25px;
}

.post-title-detail {
    font-family: var(--font-serif);
    font-size: 2.6rem;
    line-height: 1.2;
    margin-bottom: 16px;
    font-weight: 700;
    letter-spacing: -0.02em;
}

.post-content {
    font-family: var(--font-serif);
    font-size: 1.25rem;
    line-height: 1.62;
    color: var(--text-primary);
}

.post-content p {
    margin-bottom: 28px;
}

.post-content h1, .post-content h2, .post-content h3 {
    font-family: var(--font-sans);
    color: var(--text-primary);
    margin: 48px 0 20px 0;
    font-weight: 700;
    line-height: 1.3;
}

.post-content h1 { font-size: 1.75rem; letter-spacing: -0.015em; }
.post-content h2 { font-size: 1.5rem; letter-spacing: -0.01em; }
.post-content h3 { font-size: 1.2rem; }

.post-content blockquote {
    border-left: 3px solid var(--text-primary);
    padding-left: 20px;
    margin: 32px 0;
    font-style: italic;
    color: var(--text-secondary);
    font-size: 1.3rem;
}

.post-content ul, .post-content ol {
    margin: 0 0 28px 24px;
    font-size: 1.15rem;
}

.post-content li {
    margin-bottom: 10px;
}

.post-content pre {
    background-color: #fafafa;
    border: 1px solid var(--border-color);
    padding: 18px;
    border-radius: 4px;
    overflow-x: auto;
    font-family: monospace;
    font-size: 0.9rem;
    margin-bottom: 28px;
}

@media (prefers-color-scheme: dark) {
    .post-content pre {
        background-color: #1a1a1a;
    }
}

.post-content hr {
    border: 0;
    height: 1px;
    background-color: var(--border-color);
    margin: 48px 0;
}

/* Footer style */
footer {
    border-top: 1px solid var(--border-color);
    padding: 40px 0;
    text-align: center;
    color: var(--text-muted);
    font-size: 0.85rem;
    margin-top: 80px;
}

.no-posts {
    text-align: center;
    padding: 50px;
    color: var(--text-secondary);
    font-size: 1rem;
    border: 1px dashed var(--border-color);
    border-radius: 6px;
}

/* Gallery styles */
.post-gallery {
    margin-top: 50px;
    border-top: 1px solid var(--border-color);
    padding-top: 30px;
}

.post-gallery h3 {
    font-family: var(--font-sans);
    font-size: 1.2rem;
    margin-bottom: 20px;
    color: var(--text-secondary);
}

.gallery-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
    gap: 16px;
}

.gallery-grid img {
    width: 100%;
    height: 140px;
    object-fit: cover;
    border-radius: 4px;
    border: 1px solid var(--border-color);
    cursor: pointer;
    transition: opacity 0.2s ease;
}

.gallery-grid img:hover {
    opacity: 0.85;
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
            public int ReadingTime { get; set; }
        }
        private async void UpdateBlogLiveUrlLink(string blogRepo)
        {
            Action<string, Uri> setUrl = (content, uri) =>
            {
                if (BlogLiveUrlButton != null)
                {
                    BlogLiveUrlButton.Content = content;
                    BlogLiveUrlButton.NavigateUri = uri;
                }
                if (BlogPageLiveUrlButton != null)
                {
                    BlogPageLiveUrlButton.Content = content;
                    BlogPageLiveUrlButton.NavigateUri = uri;
                }
            };

            string token = GetSecureToken();
            if (string.IsNullOrEmpty(token)) token = GetSetting("GitHubToken");
            
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(blogRepo))
            {
                setUrl("Not Configured (Connect GitHub first)", null);
                return;
            }

            try
            {
                string username = GetSetting("GitHubUsername", "");
                if (string.IsNullOrEmpty(username))
                {
                    username = await GetGitHubUsername(token);
                    if (!string.IsNullOrEmpty(username))
                    {
                        SaveSetting("GitHubUsername", username);
                    }
                }

                if (!string.IsNullOrEmpty(username))
                {
                    string url = $"https://{username.ToLowerInvariant()}.github.io/{blogRepo}/";
                    setUrl(url, new Uri(url));
                }
                else
                {
                    setUrl("Unable to fetch username", null);
                }
            }
            catch (Exception ex)
            {
                setUrl($"Error: {ex.Message}", null);
            }
        }

        // ── Blog Page (NavItem) helpers ──────────────────────────────────────

        /// <summary>Populates the Blog Publisher page when the user navigates to BlogPageNavItem.</summary>
        private async void PopulateBlogPage()
        {
            try
            {
                // Mirror config values from settings
                if (BlogPageRepoTextBox != null)
                    BlogPageRepoTextBox.Text = GetSetting("BlogRepo", "");
                if (BlogPageTitleTextBox != null)
                    BlogPageTitleTextBox.Text = GetSetting("BlogTitle", "My Journal Blog");
                if (BlogPageDescTextBox != null)
                    BlogPageDescTextBox.Text = GetSetting("BlogDesc", "");
                if (BlogPageCustomCssTextBox != null)
                    BlogPageCustomCssTextBox.Text = GetSetting("BlogCustomCss", "");

                // Live URL
                string repo = GetSetting("BlogRepo", "my-journal-blog");
                if (string.IsNullOrWhiteSpace(repo)) repo = "my-journal-blog";

                if (BlogPageLiveUrlButton != null)
                {
                    string token = GetSecureToken();
                    if (string.IsNullOrEmpty(token)) token = GetSetting("GitHubToken");

                    string username = GetSetting("GitHubUsername", "");
                    if (string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(token))
                    {
                        username = await GetGitHubUsername(token);
                        if (!string.IsNullOrEmpty(username))
                        {
                            SaveSetting("GitHubUsername", username);
                        }
                    }

                    if (!string.IsNullOrEmpty(username))
                    {
                        string urlText = $"https://{username.ToLowerInvariant()}.github.io/{repo}/";
                        BlogPageLiveUrlButton.Content = urlText;
                        try { BlogPageLiveUrlButton.NavigateUri = new Uri(urlText); } catch { }
                    }
                    else
                    {
                        string urlText = $"https://[username].github.io/{repo}/";
                        BlogPageLiveUrlButton.Content = urlText;
                        try { BlogPageLiveUrlButton.NavigateUri = new Uri($"https://github.com/{repo}"); } catch { }
                    }
                }

                PopulateBlogPageList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BlogPage] PopulateBlogPage error: {ex.Message}");
            }
        }

        private void PopulateBlogPageList()
        {
            if (BlogPagePublishedList == null) return;
            var published = JournalManager.Instance.Notes
                .Where(n => n.IsBlogPublished && !n.IsDeleted)
                .OrderByDescending(n => n.DateCreated)
                .ToList();

            BlogPagePublishedList.ItemsSource = published;

            if (BlogPagePublishedCountText != null)
                BlogPagePublishedCountText.Text = $"{published.Count} {(published.Count == 1 ? "entry" : "entries")} marked for publishing";
        }

        private void BlogPageRefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            PopulateBlogPageList();
        }

        private async void BlogPageVisitBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is JournalNote note)
            {
                string token = GetSecureToken();
                if (string.IsNullOrEmpty(token)) token = GetSetting("GitHubToken");
                
                string username = GetSetting("GitHubUsername", "");
                if (string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(token))
                {
                    username = await GetGitHubUsername(token);
                    if (!string.IsNullOrEmpty(username))
                    {
                        SaveSetting("GitHubUsername", username);
                    }
                }

                if (!string.IsNullOrEmpty(username))
                {
                    string repo = GetSetting("BlogRepo", "my-journal-blog");
                    if (string.IsNullOrWhiteSpace(repo)) repo = "my-journal-blog";
                    
                    string url = $"https://{username.ToLowerInvariant()}.github.io/{repo}/posts/post_{note.Id}.html";
                    try
                    {
                        await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[BlogPage] Launch error: {ex.Message}");
                    }
                }
                else
                {
                    await ShowAlertAsync("Connect GitHub", "Please configure and sync your GitHub credentials to view live links.");
                }
            }
        }

        // ── Shared Publishing UI Helpers ─────────────────────────────────────


        private void UpdatePublishStatus(string status)
        {
            if (BlogPublishStatusText != null) BlogPublishStatusText.Text = status;
            if (BlogPageStatusText != null) BlogPageStatusText.Text = status;
        }

        private void SetPublishingUiState(bool isLoading, string status = "")
        {
            if (SettingsPublishBlogButton != null) SettingsPublishBlogButton.IsEnabled = !isLoading;
            if (BlogPublishProgressRing != null)
            {
                BlogPublishProgressRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
                BlogPublishProgressRing.IsActive = isLoading;
            }

            if (BlogPagePublishButton != null) BlogPagePublishButton.IsEnabled = !isLoading;
            if (BlogPageProgressRing != null)
            {
                BlogPageProgressRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
                BlogPageProgressRing.IsActive = isLoading;
            }

            UpdatePublishStatus(isLoading ? status : "Ready");
        }
    }
}
