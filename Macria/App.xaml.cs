using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace Macria
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // WPF, baglantilardaki sayilari varsayilan olarak en-US bicimiyle
            // yazar. Tablodaki hucreler de Windows'un dilini kullansin ki
            // ozet kartlari ve raporlarla ayni gorunsun.
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(
                    XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));
        }
    }
}
