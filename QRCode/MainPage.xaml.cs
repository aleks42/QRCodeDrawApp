using QRCodeEncoder;
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

            using (var background = assembly.GetManifestResourceStream("QRCode.back5.jpg"))
            {
                var encoder = new Encoder();
                var encoderRes = encoder.Encode(entry.Text, CorrectionLevel.H);

                var renderer = new Renderer();
                var qrCodeImgStream = renderer.Draw(encoderRes.Data, encoderRes.Version, CorrectionLevel.H, background);
                
                img.Source = ImageSource.FromStream(() => qrCodeImgStream);
            }
        }
    }
}
