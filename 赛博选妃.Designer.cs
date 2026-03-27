namespace Cyber​​ConcubineSelection
{
    partial class 赛博选妃
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
#pragma warning disable CS0414
        private System.ComponentModel.IContainer components = null;
#pragma warning restore CS0414

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>


        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(赛博选妃));
            lblFolderName = new Label();
            lblFileName = new Label();
            pictureBox1 = new PictureBox();
            pictureBox2 = new PictureBox();
            pictureBox3 = new PictureBox();
            pictureBox4 = new PictureBox();
            pictureBox5 = new PictureBox();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox2).BeginInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox3).BeginInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox4).BeginInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox5).BeginInit();
            SuspendLayout();
            // 
            // 为所有控件启用双缓冲
            foreach (Control ctrl in new Control[] { pictureBox1, pictureBox2, pictureBox3, pictureBox4, pictureBox5, lblFolderName, lblFileName })
            {
                typeof(Control).InvokeMember("DoubleBuffered",
                    System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, ctrl, new object[] { true });
            }
            // 
            // 
            // lblFolderName
            // 
            lblFolderName.Anchor = AnchorStyles.Top;
            lblFolderName.BackColor = Color.Transparent;
            lblFolderName.Font = new Font("幼圆", 26.25F, FontStyle.Bold, GraphicsUnit.Point, 134);
            lblFolderName.Location = new Point(138, 653);
            lblFolderName.Name = "lblFolderName";
            lblFolderName.Size = new Size(151, 45);
            lblFolderName.TabIndex = 2;
            lblFolderName.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // lblFileName
            // 
            lblFileName.Anchor = AnchorStyles.Top;
            lblFileName.Font = new Font("幼圆", 15.75F, FontStyle.Regular, GraphicsUnit.Point, 134);
            lblFileName.Location = new Point(110, 713);
            lblFileName.Name = "lblFileName";
            lblFileName.Size = new Size(195, 59);
            lblFileName.TabIndex = 3;
            // 
            // pictureBox1
            // 
            pictureBox1.BackColor = Color.Transparent;
            pictureBox1.BackgroundImage = (Image)resources.GetObject("pictureBox1.BackgroundImage");
            pictureBox1.BackgroundImageLayout = ImageLayout.Zoom;
            pictureBox1.Location = new Point(7, 5);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(23, 25);
            pictureBox1.TabIndex = 8;
            pictureBox1.TabStop = false;
            pictureBox1.Click += btnSelectPath_Click;
            // 
            // pictureBox2
            // 
            pictureBox2.BackColor = Color.Transparent;
            pictureBox2.BackgroundImage = (Image)resources.GetObject("pictureBox2.BackgroundImage");
            pictureBox2.BackgroundImageLayout = ImageLayout.Zoom;
            pictureBox2.Location = new Point(34, 5);
            pictureBox2.Name = "pictureBox2";
            pictureBox2.Size = new Size(29, 25);
            pictureBox2.TabIndex = 9;
            pictureBox2.TabStop = false;
            pictureBox2.Click += button1_Click;
            // 
            // pictureBox3
            // 
            pictureBox3.BackColor = Color.Transparent;
            pictureBox3.BackgroundImage = (Image)resources.GetObject("pictureBox3.BackgroundImage");
            pictureBox3.BackgroundImageLayout = ImageLayout.Zoom;
            pictureBox3.Location = new Point(94, 4);
            pictureBox3.Name = "pictureBox3";
            pictureBox3.Size = new Size(23, 27);
            pictureBox3.TabIndex = 10;
            pictureBox3.TabStop = false;
            pictureBox3.Click += btnHistory_Click;
            // 
            // pictureBox4
            // 
            pictureBox4.BackColor = Color.Transparent;
            pictureBox4.BackgroundImage = (Image)resources.GetObject("pictureBox4.BackgroundImage");
            pictureBox4.BackgroundImageLayout = ImageLayout.Zoom;
            pictureBox4.Location = new Point(121, 3);
            pictureBox4.Name = "pictureBox4";
            pictureBox4.Size = new Size(27, 30);
            pictureBox4.TabIndex = 11;
            pictureBox4.TabStop = false;
            pictureBox4.Click += btnAnnualReport_Click;
            // 
            // pictureBox5
            // 
            pictureBox5.BackColor = Color.Transparent;
            pictureBox5.BackgroundImage = (Image)resources.GetObject("pictureBox5.BackgroundImage");
            pictureBox5.BackgroundImageLayout = ImageLayout.Zoom;
            pictureBox5.Location = new Point(65, 4);
            pictureBox5.Name = "pictureBox5";
            pictureBox5.Size = new Size(24, 27);
            pictureBox5.TabIndex = 12;
            pictureBox5.TabStop = false;
            pictureBox5.Click += BtnRandomLady_Click;
            // 
            // 赛博选妃
            // 
            AutoScaleMode = AutoScaleMode.None;
            BackgroundImageLayout = ImageLayout.Zoom;
            ClientSize = new Size(434, 781);
            Controls.Add(pictureBox5);
            Controls.Add(pictureBox4);
            Controls.Add(pictureBox3);
            Controls.Add(pictureBox2);
            Controls.Add(pictureBox1);
            Controls.Add(lblFileName);
            Controls.Add(lblFolderName);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            Icon = (Icon)resources.GetObject("$this.Icon");
            MaximizeBox = false;
            Name = "赛博选妃";
            Text = "赛博选妃";
            Load += 赛博选妃_Load;
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox2).EndInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox3).EndInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox4).EndInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox5).EndInit();
            ResumeLayout(false);
        }

        #endregion
        private Label lblFolderName;
        private Label lblFileName;
        private PictureBox pictureBox1;
        private PictureBox pictureBox2;
        private PictureBox pictureBox3;
        private PictureBox pictureBox4;
        private PictureBox pictureBox5;
    }
}
