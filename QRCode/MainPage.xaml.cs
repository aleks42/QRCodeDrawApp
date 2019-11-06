using QRCodeEncoder;
using System.IO;
using System.Reflection;
using Xamarin.Forms;

namespace QRCode
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();

            entry.Text = "http://www.habr.com/";
            Refresh();
        }

        private void Button_Clicked(object sender, System.EventArgs e) => Refresh();

        private void Refresh()
        {
            var assembly = GetType().GetTypeInfo().Assembly;

            using (Stream background = assembly.GetManifestResourceStream("QRCode.back5.jpg"))
            {
                var correctionLevel = CorrectionLevel.H;
                var encoder = new Encoder();
                var data = encoder.Encode(entry.Text, correctionLevel);

                var renderer = new Renderer();
                var stream = renderer.Draw(data.Data, data.Version, CorrectionLevel.H, background);
                img.Source = ImageSource.FromStream(() => stream);
            }
        }
    }
}
