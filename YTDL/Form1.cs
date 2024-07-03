using TechnikAgSongrequest;
using YTDL.Properties;

namespace YTDL
{
    public partial class Form1 : Form
    {
        public static string currentDLtype = "video";
        public static string currentDLres = "highest";
        public static bool currentSubTitleEnabled = false;
        public static bool currentMetadataEnabled = true;
        public static bool currentAddToPlaylist = false;
        public static string dlPath = "downloadedvideos/";
        public static bool spotify_mode = false;

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private void toggle_mode()
        {
            spotify_mode = !spotify_mode;
            if (spotify_mode)
            {
                Text = "SDL by leMineGaming";
                panel1.BackColor = Color.FromArgb(38, 184, 33);
                label1.Text = "SDL by leMineGaming";
                pictureBox1.Image = Resources.icons8_spotify_240;
                kryptonTextBox1.CueHint.CueHintText = "Enter Spotify URL to download";
                kryptonButton2.Visible = false;
                kryptonButton1.Visible = false;
                BackColor = Color.FromArgb(24, 62, 17);
                kryptonTextBox1.Refresh();
            }
            else
            {
                Text = "YTDL by leMineGaming";
                panel1.BackColor = Color.FromArgb(111, 26, 7);
                label1.Text = "YTDL by leMineGaming";
                pictureBox1.Image = Resources.icons8_youtube_240;
                kryptonTextBox1.CueHint.CueHintText = "Enter Youtube / Youtube Music URL to download";
                kryptonButton2.Visible = true;
                kryptonButton1.Visible = true;
                BackColor = Color.FromArgb(43, 33, 24);
                kryptonTextBox1.Refresh();
            }
        }

        private async void kryptonButton3_Click(object sender, EventArgs e)
        {
            if (!spotify_mode)
            {
                YouTubeService _ytservice = new YouTubeService();
                string[] resultRES = await _ytservice.GetAvailableResolutionsAsync(kryptonTextBox1.Text);
                string formattedREs = "AvailableResolutions: \n";
                for (int i = 0; i < resultRES.Length; i++)
                {
                    formattedREs = formattedREs + resultRES[i] + "\n";
                }
                MessageBox.Show(formattedREs);
                await _ytservice.DownloadContentAsync(kryptonTextBox1.Text, dlPath);
            }
        }

        private void pictureBox1_Click(object sender, EventArgs e)
        {
            toggle_mode();
        }
    }
}
