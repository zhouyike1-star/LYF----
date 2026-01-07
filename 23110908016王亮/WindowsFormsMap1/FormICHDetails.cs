using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ESRI.ArcGIS.Geodatabase;

namespace WindowsFormsMap1
{
    public partial class FormICHDetails : Form
    {
        private IFeature _feature;

        public FormICHDetails(IFeature feature)
        {
            InitializeComponent();
            this._feature = feature;
            LoadProperties();
        }

        private void LoadProperties()
        {
            try
            {
                DataTable dt = new DataTable();
                dt.Columns.Add("字段项");
                dt.Columns.Add("内容值");

                IFields fields = _feature.Fields;
                for (int i = 0; i < fields.FieldCount; i++)
                {
                    IField field = fields.get_Field(i);
                    // 过滤掉几个不适合展示的内部字段
                    if (field.Type == esriFieldType.esriFieldTypeGeometry || 
                        field.Name.ToLower() == "shape" || 
                        field.Name.ToLower() == "fid") continue;

                    object val = _feature.get_Value(i);
                    dt.Rows.Add(field.AliasName, val == null ? "" : val.ToString());
                }

                dataGridView1.DataSource = dt;
                
                // 尝试抓取名称作为标题
                int nameIdx = _feature.Fields.FindField("名称");
                if (nameIdx != -1)
                {
                    this.Text = "非遗详情: " + _feature.get_Value(nameIdx).ToString();
                }

                CheckForMultimedia();
            }
            catch (Exception ex)
            {
                MessageBox.Show("加载详情失败: " + ex.Message);
            }
        }

        private void CheckForMultimedia()
        {
            // 检查常见的多媒体字段
            string[] mediaFields = { "图片", "照片", "Photo", "Image", "Pic", "视频", "Video" };
            string mediaPath = "";
            bool isVideo = false;

            foreach (var f in mediaFields)
            {
                int idx = _feature.Fields.FindField(f);
                if (idx != -1)
                {
                    object val = _feature.get_Value(idx);
                    if (val != null && !string.IsNullOrEmpty(val.ToString()))
                    {
                        mediaPath = val.ToString();
                        if (f.Contains("视频") || f.Contains("Video")) isVideo = true;
                        break;
                    }
                }
            }

            // [Beautify] 使用 SplitContainer 分割布局
            if (!string.IsNullOrEmpty(mediaPath) && System.IO.File.Exists(mediaPath))
            {
                // 1. 创建分割容器
                SplitContainer split = new SplitContainer();
                split.Dock = DockStyle.Fill;
                split.Orientation = Orientation.Horizontal;
                split.SplitterDistance = 250; // 图片区域高度
                this.Controls.Add(split);

                // 2. 移动 DataGridView 到下半部分
                if (dataGridView1 != null)
                {
                    this.Controls.Remove(dataGridView1);
                    split.Panel2.Controls.Add(dataGridView1);
                    dataGridView1.Dock = DockStyle.Fill;
                }

                // 3. 上半部分显示多媒体
                if (!isVideo)
                {
                    PictureBox pb = new PictureBox();
                    pb.ImageLocation = mediaPath;
                    pb.SizeMode = PictureBoxSizeMode.Zoom;
                    pb.Dock = DockStyle.Fill;
                    pb.BackColor = Color.Black; // 影院模式背景
                    split.Panel1.Controls.Add(pb);

                    // 双击打开原图
                    pb.DoubleClick += (s, e) => { System.Diagnostics.Process.Start(mediaPath); };
                    TooltipHelper.SetToolTip(pb, "双击查看原图");
                }
                else
                {
                    // 视频用一个按钮代替，点击播放
                    Button btnPlay = new Button();
                    btnPlay.Text = "🎥 点击播放关联视频";
                    btnPlay.Font = new Font("微软雅黑", 14, FontStyle.Bold);
                    btnPlay.Dock = DockStyle.Fill;
                    btnPlay.Click += (s, e) => { System.Diagnostics.Process.Start(mediaPath); };
                    split.Panel1.Controls.Add(btnPlay);
                }

                // [Fix] 调整窗体大小以适应内容
                this.Width = 600;
                this.Height = 700;
                this.CenterToScreen();
            }
        }

        // 简单的 Tooltip 辅助
        private static class TooltipHelper 
        {
            public static void SetToolTip(Control ctrl, string text)
            {
                ToolTip tt = new ToolTip();
                tt.SetToolTip(ctrl, text);
            }
        }

        private void BtnClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}
