using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;
using YoutubeExplode.Videos.ClosedCaptions;
using YoutubeExplode.Exceptions;
using System.IO;
using System.Diagnostics;
using System.Net.Http;

namespace TechnikAgSongrequest
{
    internal class YouTubeService
    {
        private readonly YoutubeClient _youtubeClient;
        private readonly HttpClient _httpClient;

        public YouTubeService()
        {
            _youtubeClient = new YoutubeClient();
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri("https://api.lyrics.ovh/v1");
        }

        public static string ExtractVideoId(string url)
        {
            // Define regex patterns for different types of YouTube URLs
            string[] patterns = new string[]
            {
            @"(?:https?://)?(?:www\.)?youtube\.com/watch\?v=([a-zA-Z0-9_-]{11})",  // Standard URL
            @"(?:https?://)?youtu\.be/([a-zA-Z0-9_-]{11})",                       // Shortened URL
            @"(?:https?://)?(?:www\.)?youtube\.com/embed/([a-zA-Z0-9_-]{11})",     // Embed URL
            @"(?:https?://)?(?:www\.)?youtube\.com/v/([a-zA-Z0-9_-]{11})",         // /v/ URL
            @"(?:https?://)?(?:www\.)?youtube\.com/e/([a-zA-Z0-9_-]{11})",         // /e/ URL
            @"(?:https?://)?(?:www\.)?youtube\.com/shorts/([a-zA-Z0-9_-]{11})",    // /shorts/ URL
            @"(?:https?://)?(?:www\.)?youtube\.com/live/([a-zA-Z0-9_-]{11})",      // /live/ URL
            @"(?:https?://)?(?:www\.)?music\.youtube\.com/watch\?v=([a-zA-Z0-9_-]{11})",  // Music URL
            @"(?:https?://)?m\.youtube\.com/watch\?app=desktop&v=([a-zA-Z0-9_-]{11})"     // Mobile URL
            };

            // Iterate over the patterns and search for a match
            foreach (string pattern in patterns)
            {
                var match = Regex.Match(url, pattern);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            // Return null if no match is found
            return "404";
        }

        public static string FormatSeconds(int totalSeconds)
        {
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;

            // Use PadLeft to ensure two-digit formatting for both minutes and seconds
            string formattedTime = minutes.ToString().PadLeft(2, '0') + ":" + seconds.ToString().PadLeft(2, '0');

            return formattedTime;
        }

        public async Task<(string Title, TimeSpan Length, string Creator)> GetVideoMetadataAsync(string videoUrl)
        {
            var video = await _youtubeClient.Videos.GetAsync(videoUrl);

            string title = video.Title;
            TimeSpan length = video.Duration ?? TimeSpan.Zero;
            string creator = video.Author.ChannelTitle;

            return (title, length, creator);
        }

        public string GetYouTubeVideoId(string url)
        {
            var uri = new Uri(url);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            return query["v"];
        }

        public async Task<string> DownloadVideoAsync(string videoUrl, string downloadPath, string resolution = null)
        {
            string videoId = ExtractVideoId(videoUrl);
            if (string.IsNullOrEmpty(videoId))
            {
                throw new Exception("Invalid YouTube URL. Unable to extract video ID.");
            }

            var streamManifest = await _youtubeClient.Videos.Streams.GetManifestAsync(videoId);

            IStreamInfo videoStreamInfo = null;
            if (!string.IsNullOrEmpty(resolution))
            {
                videoStreamInfo = streamManifest.GetVideoStreams()
                    .Where(s => s.VideoQuality.Label == resolution)
                    .OrderByDescending(s => s.VideoQuality.MaxHeight)
                    .FirstOrDefault();
                if (videoStreamInfo == null)
                {
                    throw new Exception($"No video stream found with resolution {resolution}.");
                }
            }
            else
            {
                videoStreamInfo = streamManifest.GetVideoStreams()
                    .OrderByDescending(s => s.VideoQuality.MaxHeight)
                    .FirstOrDefault();
            }

            var audioStreamInfo = streamManifest.GetAudioStreams()
                .OrderByDescending(s => s.Bitrate)
                .FirstOrDefault();

            if (videoStreamInfo == null || audioStreamInfo == null)
            {
                throw new Exception("No suitable video or audio stream found.");
            }

            string videoFilePath = Path.Combine(downloadPath, $"{videoId}_video.{videoStreamInfo.Container.Name}");
            string audioFilePath = Path.Combine(downloadPath, $"{videoId}_audio.{audioStreamInfo.Container.Name}");
            string outputFilePath = Path.Combine(downloadPath, $"{videoId}.mp4");

            await _youtubeClient.Videos.Streams.DownloadAsync(videoStreamInfo, videoFilePath);
            await _youtubeClient.Videos.Streams.DownloadAsync(audioStreamInfo, audioFilePath);

            await MuxVideoAndAudioWithFFmpeg(videoFilePath, audioFilePath, outputFilePath);

            File.Delete(videoFilePath);
            File.Delete(audioFilePath);

            return outputFilePath;
        }

        private async Task MuxVideoAndAudioWithFFmpeg(string videoFilePath, string audioFilePath, string outputFilePath)
        {
            try
            {
                string ffmpegPath = @"ffmpeg\ffmpeg.exe";
                string arguments = $"-i \"{videoFilePath}\" -i \"{audioFilePath}\" -c:v copy -c:a aac \"{outputFilePath}\"";

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(startInfo))
                {
                    await process.WaitForExitAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error muxing video and audio: {ex.Message}");
                throw;
            }
        }

        public async Task<string[]> GetAvailableResolutionsAsync(string videoUrl)
        {
            string videoId = ExtractVideoId(videoUrl);
            if (string.IsNullOrEmpty(videoId))
            {
                throw new Exception("Invalid YouTube URL. Unable to extract video ID.");
            }

            var streamManifest = await _youtubeClient.Videos.Streams.GetManifestAsync(videoId);
            var resolutions = streamManifest.GetVideoStreams()
                .Select(s => s.VideoQuality.Label)
                .Distinct()
                .ToArray();

            return resolutions;
        }

        public async Task<bool> AreSubtitlesAvailableAsync(string videoUrl)
        {
            string videoId = GetYouTubeVideoId(videoUrl);
            if (string.IsNullOrEmpty(videoId))
            {
                throw new Exception("Invalid YouTube URL. Unable to extract video ID.");
            }

            var captionsManifest = await _youtubeClient.Videos.ClosedCaptions.GetManifestAsync(videoId);
            return captionsManifest.Tracks.Any();
        }

        public async Task DownloadPlaylistAsync(string playlistUrl, string downloadPath)
        {
            var playlist = await _youtubeClient.Playlists.GetAsync(playlistUrl);
            var videos = await _youtubeClient.Playlists.GetVideosAsync(playlistUrl);

            foreach (var video in videos)
            {
                try
                {
                    string videoUrl = video.Url;
                    await DownloadVideoAsync(videoUrl, downloadPath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error downloading video {video.Url}: {ex.Message}");
                }
            }
        }

        public async Task<string> DownloadAndConvertVideoAsync(string videoUrl, string downloadPath)
        {
            try
            {
                string videoId = GetYouTubeVideoId(videoUrl);
                if (string.IsNullOrEmpty(videoId))
                {
                    throw new Exception("Invalid YouTube URL. Unable to extract video ID.");
                }

                string mp4FileName = $"{videoId}.mp4";
                string mp3FileName = $"{videoId}.mp3";
                string mp4FilePath = Path.Combine(downloadPath, mp4FileName);
                string mp3FilePath = Path.Combine(downloadPath, mp3FileName);

                await DownloadVideoAsync(videoUrl, downloadPath);
                await ConvertMp4ToMp3WithFFmpeg(mp4FilePath, mp3FilePath);
                if (File.Exists(mp4FilePath))
                {
                    File.Delete(mp4FilePath);
                }

                return mp3FilePath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading and converting video: {ex.Message}");
                throw;
            }
        }

        private async Task ConvertMp4ToMp3WithFFmpeg(string inputFilePath, string outputFilePath)
        {
            try
            {
                string ffmpegPath = @"ffmpeg\ffmpeg.exe";
                string arguments = $"-i \"{inputFilePath}\" -vn -acodec libmp3lame -q:a 2 \"{outputFilePath}\"";

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(startInfo))
                {
                    await process.WaitForExitAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error converting MP4 to MP3: {ex.Message}");
                throw;
            }
        }

        public async Task<string> DownloadSubtitlesAndGetTextAsync(string videoUrl, string downloadPath)
        {
            try
            {
                string videoId = GetYouTubeVideoId(videoUrl);

                var captionsManifest = await _youtubeClient.Videos.ClosedCaptions.GetManifestAsync(videoId);
                string[] preferredLanguages = { "de", "en", "fr" };

                ClosedCaptionTrackInfo subtitleTrack = null; // Adjust language code as needed

                foreach (var lang in preferredLanguages)
                {
                    subtitleTrack = captionsManifest.TryGetByLanguage(lang);
                    if (subtitleTrack != null)
                        break;
                }

                // If no preferred language tracks are found, use any available track
                if (subtitleTrack == null)
                {
                    subtitleTrack = captionsManifest.Tracks.FirstOrDefault();
                }

                if (subtitleTrack == null)
                {
                    throw new Exception("No subtitles available for this video.");
                }

                var captions = await _youtubeClient.Videos.ClosedCaptions.GetAsync(subtitleTrack);

                string subtitleText = string.Join("", captions.Captions);

                string language = subtitleTrack.Language.Name;
                string autoGenerated = subtitleTrack.IsAutoGenerated ? "Auto-generated" : "Human-generated";
                string lcid = "Lyrics: YouTube API\n";

                string fullText = $"{lcid}Language: {language}\nType: {autoGenerated}\n\n{subtitleText}";

                string subtitleFileName = $"{videoId}_subtitles.txt";
                string subtitleFilePath = Path.Combine(downloadPath, subtitleFileName);



                await File.WriteAllTextAsync(subtitleFilePath, fullText);

                // Read the text from the file
                string text = await File.ReadAllTextAsync(subtitleFilePath);

                // Delete the subtitles file after reading
                File.Delete(subtitleFilePath);

                return text;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading subtitles and getting text: {ex.Message}");
                throw;
            }
        }

        public async Task<string> GetLyricsAsync(string artist, string title)
        {
            try
            {
                string preparedTitle = Uri.EscapeDataString(title);
                string preparedArtist = Uri.EscapeDataString(artist);
                string requestUri = $"/{preparedArtist}/{preparedTitle}";

                // Log the constructed URL to debug

                HttpResponseMessage response = await _httpClient.GetAsync(_httpClient.BaseAddress + requestUri);
                response.EnsureSuccessStatusCode();

                string responseBody = await response.Content.ReadAsStringAsync();

                // Parse JSON response to get lyrics
                dynamic jsonResponse = Newtonsoft.Json.JsonConvert.DeserializeObject(responseBody);
                string lyrics = jsonResponse.lyrics;

                string completelyrics = "Lyrics: Lyrics.OVH API\n" + lyrics;

                return completelyrics;
            }
            catch (HttpRequestException ex)
            {
                MessageBox.Show($"Error fetching lyrics: {ex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
                throw;
            }
        }

        public async Task DownloadContentAsync(string url, string downloadPath, string resolution = null)
        {
            // Check if the URL is a playlist or a single video
            bool isPlaylist = Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Contains("youtube.com") && uri.AbsolutePath.Contains("playlist");

            if (isPlaylist)
            {
                await DownloadPlaylistAsync(url, downloadPath);
            }
            else
            {
                await DownloadVideoAsync(url, downloadPath, resolution);
            }
        }

        public async Task<string> GetThumbnailUrlAsync(string videoUrl)
        {
            var video = await _youtubeClient.Videos.GetAsync(videoUrl);
            return video.Thumbnails.GetWithHighestResolution().Url;
        }
    }
}
