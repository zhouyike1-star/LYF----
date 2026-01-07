using System;
using System.Windows.Forms;

namespace WindowsFormsMap1
{
    public partial class FormRoamingSetting : Form
    {
        public enum RoamDirection
        {
            LeftToRight,
            RightToLeft,
            TopToBottom,
            BottomToTop
        }

        public RoamDirection SelectedDirection { get; private set; }
        public double SelectedDuration { get; private set; }

        private ComboBox cmbDirection;
        private ComboBox cmbSpeed;
        private Button btnStart;
        private Button btnCancel;
        private Label lblDirection;
        private Label lblSpeed;

        public FormRoamingSetting()
        {
            InitializeComponent();
            InitData();
        }

        private void InitializeComponent()
        {
            this.cmbDirection = new System.Windows.Forms.ComboBox();
            this.cmbSpeed = new System.Windows.Forms.ComboBox();
            this.btnStart = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.lblDirection = new System.Windows.Forms.Label();
            this.lblSpeed = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // cmbDirection
            // 
            this.cmbDirection.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbDirection.FormattingEnabled = true;
            this.cmbDirection.Location = new System.Drawing.Point(111, 26);
            this.cmbDirection.Name = "cmbDirection";
            this.cmbDirection.Size = new System.Drawing.Size(149, 20);
            this.cmbDirection.TabIndex = 0;
            // 
            // cmbSpeed
            // 
            this.cmbSpeed.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbSpeed.FormattingEnabled = true;
            this.cmbSpeed.Location = new System.Drawing.Point(111, 63);
            this.cmbSpeed.Name = "cmbSpeed";
            this.cmbSpeed.Size = new System.Drawing.Size(149, 20);
            this.cmbSpeed.TabIndex = 1;
            // 
            // btnStart
            // 
            this.btnStart.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.btnStart.Location = new System.Drawing.Point(54, 110);
            this.btnStart.Name = "btnStart";
            this.btnStart.Size = new System.Drawing.Size(75, 23);
            this.btnStart.TabIndex = 2;
            this.btnStart.Text = "开始漫游";
            this.btnStart.UseVisualStyleBackColor = true;
            this.btnStart.Click += new System.EventHandler(this.btnStart_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(157, 110);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(75, 23);
            this.btnCancel.TabIndex = 3;
            this.btnCancel.Text = "取消";
            this.btnCancel.UseVisualStyleBackColor = true;
            // 
            // lblDirection
            // 
            this.lblDirection.AutoSize = true;
            this.lblDirection.Location = new System.Drawing.Point(52, 29);
            this.lblDirection.Name = "lblDirection";
            this.lblDirection.Size = new System.Drawing.Size(53, 12);
            this.lblDirection.TabIndex = 4;
            this.lblDirection.Text = "漫游方向";
            // 
            // lblSpeed
            // 
            this.lblSpeed.AutoSize = true;
            this.lblSpeed.Location = new System.Drawing.Point(52, 66);
            this.lblSpeed.Name = "lblSpeed";
            this.lblSpeed.Size = new System.Drawing.Size(53, 12);
            this.lblSpeed.TabIndex = 5;
            this.lblSpeed.Text = "漫游速度";
            // 
            // FormRoamingSetting
            // 
            this.AcceptButton = this.btnStart;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(305, 161);
            this.Controls.Add(this.lblSpeed);
            this.Controls.Add(this.lblDirection);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnStart);
            this.Controls.Add(this.cmbSpeed);
            this.Controls.Add(this.cmbDirection);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "FormRoamingSetting";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "自动漫游设置";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private void InitData()
        {
            // 方向
            cmbDirection.Items.Add("从左到右 (Left -> Right)");
            cmbDirection.Items.Add("从右到左 (Right -> Left)");
            cmbDirection.Items.Add("从上到下 (Top -> Bottom)");
            cmbDirection.Items.Add("从下到上 (Bottom -> Top)");
            cmbDirection.SelectedIndex = 0;

            // 速度
            cmbSpeed.Items.Add("慢速浏览 (60秒)");
            cmbSpeed.Items.Add("正常速度 (30秒)");
            cmbSpeed.Items.Add("快速飞掠 (10秒)");
            cmbSpeed.SelectedIndex = 1;
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            // 解析方向
            switch (cmbDirection.SelectedIndex)
            {
                case 0: SelectedDirection = RoamDirection.LeftToRight; break;
                case 1: SelectedDirection = RoamDirection.RightToLeft; break;
                case 2: SelectedDirection = RoamDirection.TopToBottom; break;
                case 3: SelectedDirection = RoamDirection.BottomToTop; break;
                default: SelectedDirection = RoamDirection.LeftToRight; break;
            }

            // 解析速度
            switch (cmbSpeed.SelectedIndex)
            {
                case 0: SelectedDuration = 60.0; break;
                case 1: SelectedDuration = 30.0; break;
                case 2: SelectedDuration = 10.0; break;
                default: SelectedDuration = 30.0; break;
            }
        }
    }
}
