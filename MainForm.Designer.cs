namespace GrzMotion
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose( bool disposing )
        {
            if ( disposing && ( components != null ) )
            {
                components.Dispose( );
            }
            base.Dispose( disposing );
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent( )
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            this.devicesCombo = new System.Windows.Forms.ComboBox();
            this.uvcResolutionsCombo = new System.Windows.Forms.ComboBox();
            this.connectButton = new System.Windows.Forms.Button();
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.snapshotButton = new System.Windows.Forms.Button();
            this.buttonSettings = new System.Windows.Forms.Button();
            this.hScrollBarExposure = new System.Windows.Forms.HScrollBar();
            this.buttonDefaultCameraProps = new System.Windows.Forms.Button();
            this.buttonAutoExposure = new System.Windows.Forms.Button();
            this.buttonCameraProperties = new System.Windows.Forms.Button();
            this.timerFlowControl = new System.Windows.Forms.Timer(this.components);
            this.tableLayoutPanelMain = new System.Windows.Forms.TableLayoutPanel();
            this.panel4 = new System.Windows.Forms.Panel();
            this.panelGraphs = new System.Windows.Forms.Panel();
            this.tableLayoutPanelGraphs = new System.Windows.Forms.TableLayoutPanel();
            this.panelCameraButtons = new System.Windows.Forms.TableLayoutPanel();
            this.timerCheckTelegramLiveTick = new System.Windows.Forms.Timer(this.components);
            this.tableLayoutPanelMain.SuspendLayout();
            this.panel4.SuspendLayout();
            this.panelGraphs.SuspendLayout();
            this.panelCameraButtons.SuspendLayout();
            this.SuspendLayout();
            // 
            // devicesCombo
            // 
            this.devicesCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.devicesCombo.FormattingEnabled = true;
            this.devicesCombo.Location = new System.Drawing.Point(62, 5);
            this.devicesCombo.Margin = new System.Windows.Forms.Padding(3, 5, 3, 3);
            this.devicesCombo.Name = "devicesCombo";
            this.devicesCombo.Size = new System.Drawing.Size(136, 21);
            this.devicesCombo.TabIndex = 1;
            this.toolTip.SetToolTip(this.devicesCombo, "available video sources");
            this.devicesCombo.SelectedIndexChanged += new System.EventHandler(this.devicesCombo_SelectedIndexChanged);
            // 
            // uvcResolutionsCombo
            // 
            this.uvcResolutionsCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.uvcResolutionsCombo.FormattingEnabled = true;
            this.uvcResolutionsCombo.Location = new System.Drawing.Point(62, 35);
            this.uvcResolutionsCombo.Margin = new System.Windows.Forms.Padding(3, 5, 3, 3);
            this.uvcResolutionsCombo.Name = "uvcResolutionsCombo";
            this.uvcResolutionsCombo.Size = new System.Drawing.Size(136, 21);
            this.uvcResolutionsCombo.TabIndex = 3;
            this.toolTip.SetToolTip(this.uvcResolutionsCombo, "available camera resolutions");
            this.uvcResolutionsCombo.SelectedIndexChanged += new System.EventHandler(this.uvcResolutionsCombo_SelectedIndexChanged);
            // 
            // connectButton
            // 
            this.connectButton.Location = new System.Drawing.Point(337, 3);
            this.connectButton.Name = "connectButton";
            this.tableLayoutPanelMain.SetRowSpan(this.connectButton, 2);
            this.connectButton.Size = new System.Drawing.Size(74, 54);
            this.connectButton.TabIndex = 6;
            this.connectButton.Text = "&Start";
            this.toolTip.SetToolTip(this.connectButton, "start/stop camera");
            this.connectButton.UseVisualStyleBackColor = true;
            this.connectButton.Click += new System.EventHandler(this.connectButton_Click);
            // 
            // toolTip
            // 
            this.toolTip.AutoPopDelay = 5000;
            this.toolTip.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(192)))), ((int)(((byte)(255)))), ((int)(((byte)(192)))));
            this.toolTip.InitialDelay = 100;
            this.toolTip.ReshowDelay = 100;
            // 
            // snapshotButton
            // 
            this.snapshotButton.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("snapshotButton.BackgroundImage")));
            this.snapshotButton.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center;
            this.snapshotButton.Location = new System.Drawing.Point(414, 3);
            this.snapshotButton.Margin = new System.Windows.Forms.Padding(0, 3, 0, 0);
            this.snapshotButton.Name = "snapshotButton";
            this.snapshotButton.Size = new System.Drawing.Size(35, 27);
            this.snapshotButton.TabIndex = 9;
            this.toolTip.SetToolTip(this.snapshotButton, "capture a snapshot image");
            this.snapshotButton.UseVisualStyleBackColor = true;
            this.snapshotButton.Click += new System.EventHandler(this.snapshotButton_Click);
            // 
            // buttonSettings
            // 
            this.buttonSettings.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("buttonSettings.BackgroundImage")));
            this.buttonSettings.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center;
            this.buttonSettings.Location = new System.Drawing.Point(0, 0);
            this.buttonSettings.Margin = new System.Windows.Forms.Padding(3, 0, 0, 0);
            this.buttonSettings.Name = "buttonSettings";
            this.buttonSettings.Size = new System.Drawing.Size(35, 27);
            this.buttonSettings.TabIndex = 0;
            this.toolTip.SetToolTip(this.buttonSettings, "app settings");
            this.buttonSettings.UseVisualStyleBackColor = true;
            this.buttonSettings.Click += new System.EventHandler(this.buttonSettings_Click);
            // 
            // hScrollBarExposure
            // 
            this.hScrollBarExposure.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.hScrollBarExposure.LargeChange = 1;
            this.hScrollBarExposure.Location = new System.Drawing.Point(201, 5);
            this.hScrollBarExposure.Margin = new System.Windows.Forms.Padding(0, 5, 7, 0);
            this.hScrollBarExposure.Maximum = 1;
            this.hScrollBarExposure.Minimum = -10;
            this.hScrollBarExposure.Name = "hScrollBarExposure";
            this.hScrollBarExposure.Size = new System.Drawing.Size(126, 17);
            this.hScrollBarExposure.TabIndex = 11;
            this.toolTip.SetToolTip(this.hScrollBarExposure, "manually set camera exposure time");
            this.hScrollBarExposure.Value = -5;
            this.hScrollBarExposure.Scroll += new System.Windows.Forms.ScrollEventHandler(this.hScrollBarExposure_Scroll);
            // 
            // buttonDefaultCameraProps
            // 
            this.buttonDefaultCameraProps.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.buttonDefaultCameraProps.Location = new System.Drawing.Point(3, 3);
            this.buttonDefaultCameraProps.Name = "buttonDefaultCameraProps";
            this.buttonDefaultCameraProps.Size = new System.Drawing.Size(60, 24);
            this.buttonDefaultCameraProps.TabIndex = 0;
            this.buttonDefaultCameraProps.Text = "default";
            this.toolTip.SetToolTip(this.buttonDefaultCameraProps, "reset all camera properties to default");
            this.buttonDefaultCameraProps.UseVisualStyleBackColor = true;
            this.buttonDefaultCameraProps.Click += new System.EventHandler(this.buttonDefaultCameraProps_Click);
            // 
            // buttonAutoExposure
            // 
            this.buttonAutoExposure.Location = new System.Drawing.Point(69, 3);
            this.buttonAutoExposure.Name = "buttonAutoExposure";
            this.buttonAutoExposure.Size = new System.Drawing.Size(60, 23);
            this.buttonAutoExposure.TabIndex = 1;
            this.buttonAutoExposure.Text = "auto";
            this.toolTip.SetToolTip(this.buttonAutoExposure, "set camera exposure time to automatic");
            this.buttonAutoExposure.UseVisualStyleBackColor = true;
            this.buttonAutoExposure.Click += new System.EventHandler(this.buttonAutoExposure_Click);
            // 
            // buttonCameraProperties
            // 
            this.buttonCameraProperties.Location = new System.Drawing.Point(3, 3);
            this.buttonCameraProperties.Name = "buttonCameraProperties";
            this.tableLayoutPanelMain.SetRowSpan(this.buttonCameraProperties, 2);
            this.buttonCameraProperties.Size = new System.Drawing.Size(53, 53);
            this.buttonCameraProperties.TabIndex = 10;
            this.buttonCameraProperties.Text = "Camera Settings";
            this.toolTip.SetToolTip(this.buttonCameraProperties, "camera specific settings");
            this.buttonCameraProperties.UseVisualStyleBackColor = true;
            this.buttonCameraProperties.Click += new System.EventHandler(this.buttonCameraProperties_Click);
            // 
            // timerFlowControl
            // 
            this.timerFlowControl.Enabled = true;
            this.timerFlowControl.Interval = 30000;
            this.timerFlowControl.Tick += new System.EventHandler(this.timerFlowControl_Tick);
            // 
            // tableLayoutPanelMain
            // 
            this.tableLayoutPanelMain.ColumnCount = 5;
            this.tableLayoutPanelMain.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 59F));
            this.tableLayoutPanelMain.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 142F));
            this.tableLayoutPanelMain.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 133F));
            this.tableLayoutPanelMain.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 80F));
            this.tableLayoutPanelMain.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanelMain.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.tableLayoutPanelMain.Controls.Add(this.buttonCameraProperties, 0, 0);
            this.tableLayoutPanelMain.Controls.Add(this.connectButton, 3, 0);
            this.tableLayoutPanelMain.Controls.Add(this.snapshotButton, 4, 0);
            this.tableLayoutPanelMain.Controls.Add(this.hScrollBarExposure, 2, 0);
            this.tableLayoutPanelMain.Controls.Add(this.panel4, 4, 1);
            this.tableLayoutPanelMain.Controls.Add(this.uvcResolutionsCombo, 1, 1);
            this.tableLayoutPanelMain.Controls.Add(this.panelGraphs, 0, 2);
            this.tableLayoutPanelMain.Controls.Add(this.panelCameraButtons, 2, 1);
            this.tableLayoutPanelMain.Controls.Add(this.devicesCombo, 1, 0);
            this.tableLayoutPanelMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableLayoutPanelMain.Location = new System.Drawing.Point(0, 0);
            this.tableLayoutPanelMain.Margin = new System.Windows.Forms.Padding(0);
            this.tableLayoutPanelMain.Name = "tableLayoutPanelMain";
            this.tableLayoutPanelMain.RowCount = 3;
            this.tableLayoutPanelMain.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 30F));
            this.tableLayoutPanelMain.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 30F));
            this.tableLayoutPanelMain.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanelMain.Size = new System.Drawing.Size(454, 400);
            this.tableLayoutPanelMain.TabIndex = 11;
            this.tableLayoutPanelMain.MouseHover += new System.EventHandler(this.tableLayoutPanel_MouseHover);
            // 
            // panel4
            // 
            this.panel4.Controls.Add(this.buttonSettings);
            this.panel4.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panel4.Location = new System.Drawing.Point(414, 30);
            this.panel4.Margin = new System.Windows.Forms.Padding(0);
            this.panel4.Name = "panel4";
            this.panel4.Size = new System.Drawing.Size(40, 30);
            this.panel4.TabIndex = 19;
            // 
            // panelGraphs
            // 
            this.tableLayoutPanelMain.SetColumnSpan(this.panelGraphs, 5);
            this.panelGraphs.Controls.Add(this.tableLayoutPanelGraphs);
            this.panelGraphs.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelGraphs.Location = new System.Drawing.Point(0, 60);
            this.panelGraphs.Margin = new System.Windows.Forms.Padding(0);
            this.panelGraphs.Name = "panelGraphs";
            this.panelGraphs.Size = new System.Drawing.Size(454, 340);
            this.panelGraphs.TabIndex = 20;
            // 
            // tableLayoutPanelGraphs
            // 
            this.tableLayoutPanelGraphs.ColumnCount = 1;
            this.tableLayoutPanelGraphs.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanelGraphs.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableLayoutPanelGraphs.Location = new System.Drawing.Point(0, 0);
            this.tableLayoutPanelGraphs.Margin = new System.Windows.Forms.Padding(0);
            this.tableLayoutPanelGraphs.Name = "tableLayoutPanelGraphs";
            this.tableLayoutPanelGraphs.RowCount = 1;
            this.tableLayoutPanelGraphs.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanelGraphs.Size = new System.Drawing.Size(454, 340);
            this.tableLayoutPanelGraphs.TabIndex = 13;
            // 
            // panelCameraButtons
            // 
            this.panelCameraButtons.ColumnCount = 2;
            this.panelCameraButtons.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.panelCameraButtons.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.panelCameraButtons.Controls.Add(this.buttonDefaultCameraProps, 0, 0);
            this.panelCameraButtons.Controls.Add(this.buttonAutoExposure, 1, 0);
            this.panelCameraButtons.Location = new System.Drawing.Point(201, 30);
            this.panelCameraButtons.Margin = new System.Windows.Forms.Padding(0);
            this.panelCameraButtons.Name = "panelCameraButtons";
            this.panelCameraButtons.RowCount = 1;
            this.panelCameraButtons.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.panelCameraButtons.Size = new System.Drawing.Size(133, 30);
            this.panelCameraButtons.TabIndex = 21;
            this.panelCameraButtons.MouseHover += new System.EventHandler(this.tableLayoutPanel_MouseHover);
            // 
            // timerCheckTelegramLiveTick
            // 
            this.timerCheckTelegramLiveTick.Interval = 30000;
            this.timerCheckTelegramLiveTick.Tick += new System.EventHandler(this.timerCheckTelegramLiveTick_Tick);
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(454, 400);
            this.Controls.Add(this.tableLayoutPanelMain);
            this.DoubleBuffered = true;
            this.MaximizeBox = false;
            this.MinimumSize = new System.Drawing.Size(470, 439);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.Shown += new System.EventHandler(this.MainForm_Shown);
            this.ResizeBegin += new System.EventHandler(this.MainForm_ResizeBegin);
            this.ResizeEnd += new System.EventHandler(this.MainForm_ResizeEnd);
            this.Resize += new System.EventHandler(this.MainForm_Resize);
            this.tableLayoutPanelMain.ResumeLayout(false);
            this.panel4.ResumeLayout(false);
            this.panelGraphs.ResumeLayout(false);
            this.panelCameraButtons.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ComboBox devicesCombo;
        private System.Windows.Forms.ComboBox uvcResolutionsCombo;
        private System.Windows.Forms.Button connectButton;
        private System.Windows.Forms.ToolTip toolTip;
        private System.Windows.Forms.Button snapshotButton;
        private System.Windows.Forms.Button buttonCameraProperties;
        private System.Windows.Forms.Timer timerFlowControl;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanelMain;
        private System.Windows.Forms.Panel panel4;
        private System.Windows.Forms.Button buttonSettings;
        private System.Windows.Forms.Panel panelGraphs;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanelGraphs;
        private System.Windows.Forms.HScrollBar hScrollBarExposure;
        private System.Windows.Forms.Timer timerCheckTelegramLiveTick;
        private System.Windows.Forms.TableLayoutPanel panelCameraButtons;
        private System.Windows.Forms.Button buttonAutoExposure;
        private System.Windows.Forms.Button buttonDefaultCameraProps;
        //        private System.Windows.Forms.PictureBox pictureBox;
    }
}

