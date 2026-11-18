using System.ComponentModel.Composition;
using System.Windows;

namespace FitsAnalyse.Resources {

    [Export(typeof(ResourceDictionary))]
    public partial class Dockables : ResourceDictionary {

        public Dockables() {
            InitializeComponent();
        }
    }
}
