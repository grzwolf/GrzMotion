
using System.Drawing;
using System.Windows.Forms;

namespace GrzMotion {
    public partial class ImageBox : UserControl {

        private Bitmap bmp;

        // public get set
        public Bitmap Image {
            get {
                return bmp;
            }
            set {
                bmp = value;
                this.Invalidate();
            }
        }

        public ImageBox() {
            InitializeComponent();
            this.DoubleBuffered = true;
            this.Paint += ImageBox_OnPaint;
        }

        private void ImageBox_OnPaint(object sender, PaintEventArgs pea) {
            if ( bmp != null ) {
                pea.Graphics.DrawImage(bmp, this.ClientRectangle);
            }
        }
    }
}
