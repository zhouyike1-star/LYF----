using ESRI.ArcGIS.Carto;
using ESRI.ArcGIS.Controls;
using ESRI.ArcGIS.Display;
using ESRI.ArcGIS.Geodatabase;
using ESRI.ArcGIS.Geometry;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Windows.Forms;

namespace WindowsFormsMap1
{
    /// <summary>
    /// Form1 的可视化/演示模式相关逻辑
    /// </summary>
    public partial class Form1
    {
        private VisualHelper _visualHelper;
        private IFeatureLayer _heatmapLayer;
        private string _timeField;

        /// <summary>
        /// [组员 E 接口] 初始化沉浸式演示模块
        /// 由组长在 Form1_Load 中统一调用
        /// </summary>
        public void InitVisualModule()
        {
            try
            {
                // 1. 初始化 Helper
                _visualHelper = new VisualHelper(this.axMapControlVisual);

                // 2. 动态添加菜单项 (防止破坏 Designer)
                // 查找主菜单
                if (this.menuStrip1 != null)
                {
                    ToolStripMenuItem visualMenu = new ToolStripMenuItem("沉浸式演示(Visual)");
                    
                    ToolStripMenuItem itemHeatmap = new ToolStripMenuItem("生成热力图 (Heatmap)");
                    itemHeatmap.Click += (s, e) => 
                    {
                        // [Fix] 自动同步逻辑
                        if (axMapControlVisual.LayerCount == 0 && axMapControl2.LayerCount > 0)
                        {
                            SyncToVisualMode();
                        }

                        // 查找点图层和面图层
                        IFeatureLayer pointLayer = null;
                        IFeatureLayer polygonLayer = null;

                        for (int i = 0; i < axMapControlVisual.LayerCount; i++)
                        {
                            IFeatureLayer fl = axMapControlVisual.get_Layer(i) as IFeatureLayer;
                            if (fl == null) continue;

                            if (fl.FeatureClass.ShapeType == esriGeometryType.esriGeometryPoint)
                            {
                                if (pointLayer == null) pointLayer = fl; // 取第一个点图层
                            }
                            else if (fl.FeatureClass.ShapeType == esriGeometryType.esriGeometryPolygon)
                            {
                                // 尝试匹配名称 "shiqu"
                                if (fl.Name.ToLower().Contains("shiqu") || fl.Name.Contains("市区") || fl.Name.Contains("行政"))
                                {
                                    polygonLayer = fl;
                                }
                            }
                        }
                        
                        // 如果没找到特定的 shiqu 图层，就取第一个面图层兜底
                        if (polygonLayer == null)
                        {
                             for (int i = 0; i < axMapControlVisual.LayerCount; i++)
                             {
                                IFeatureLayer fl = axMapControlVisual.get_Layer(i) as IFeatureLayer;
                                if (fl != null && fl.FeatureClass.ShapeType == esriGeometryType.esriGeometryPolygon)
                                {
                                    polygonLayer = fl;
                                    break;
                                }
                             }
                        }

                        if (pointLayer != null && polygonLayer != null)
                        {
                            // 自动切换到沉浸式演示标签页 (索引2)
                            if (tabControl1.SelectedIndex != 2) tabControl1.SelectedIndex = 2;

                            // 调用新的空间统计热力图逻辑
                            _visualHelper.CreateSpatialStatisticHeatmap(pointLayer, polygonLayer);
                        }
                        else
                        {
                            MessageBox.Show("生成失败：需要同时存在[点图层]和[面图层(shiqu)]。\n当前未找到匹配的图层。", "提示");
                        }
                    };

                    ToolStripMenuItem itemFly = new ToolStripMenuItem("开启/停止 漫游");
                    itemFly.Click += (s, e) => 
                    {
                        // 1. 停止之前的漫游 (如果有)
                        _visualHelper.StopFlyOver();

                        // 2. 显示设置窗口
                        FormRoamingSetting frm = new FormRoamingSetting();
                        if (frm.ShowDialog() == DialogResult.OK)
                        {
                            // 获取用户设置
                            var direction = frm.SelectedDirection;
                            var duration = frm.SelectedDuration;

                            // 3. 自动切换到沉浸式演示标签页
                            if (tabControl1.SelectedIndex != 2) tabControl1.SelectedIndex = 2;
                            
                            // 4. 准备视图：先全图，然后稍微缩小一点，以便有漫游空间
                            axMapControlVisual.Extent = axMapControlVisual.FullExtent;
                            IEnvelope env = axMapControlVisual.Extent;
                            // 缩小视图到一半大小 (Zoom In)，这样才有漫游(Pan)的效果
                            env.Expand(0.5, 0.5, true); 
                            axMapControlVisual.Extent = env;

                            // 5. 开始漫游
                            _visualHelper.StartFlyOver(direction, duration);
                        }
                    };

                    // [New] 指针工具 - 方便用户点击查询
                    ToolStripMenuItem itemPointer = new ToolStripMenuItem("切换鼠标为指针 (Pointer/Select)");
                    itemPointer.Click += (s, e) => { axMapControlVisual.CurrentTool = null; };

                    visualMenu.DropDownItems.Add(itemHeatmap);
                    visualMenu.DropDownItems.Add(itemFly);
                    visualMenu.DropDownItems.Add(itemPointer);
                    
                    this.menuStrip1.Items.Add(visualMenu);
                }

                // MessageBox.Show("组员 E (沉浸式演示) 模块初始化就绪。\n新增菜单: [沉浸式演示]", "系统消息");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Visual Module Init Error: " + ex.Message);
            }
        }
        private void TabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            TabPage selectedTab = tabControl1.SelectedTab;
            bool isVisual = selectedTab.Text.Contains("可视化") || tabControl1.SelectedIndex == 2;
            bool isLayout = selectedTab.Text.Contains("布局") || tabControl1.SelectedIndex == 1;

            // 联动显隐 UI
            // 演示模式下隐藏左侧 TOC 和分割条，使地图充满主体
            this.axTOCControl2.Visible = !isVisual;
            this.splitter1.Visible = !isVisual;
            this.splitter2.Visible = !isVisual;

            // 菜单栏和状态栏始终保持可见以便操作
            this.menuStrip1.Visible = true;
            this.statusStrip1.Visible = true;

            if (isLayout) _layoutHelper.SynchronizeMap();
            if (isVisual) SyncToVisualMode();
        }

        private void SyncToVisualMode()
        {
            if (axMapControlVisual == null || axMapControl2 == null) return;
            try
            {
                axMapControlVisual.ClearLayers();
                // [修复] 必须反向遍历添加图层，因为AddLayer添加到顶部
                // 原顺序: 0=Point(Top), 1=Polygon(Bottom)
                // 反向:Add(1)->[1]; Add(0)->[0,1] -> 正确
                for (int i = axMapControl2.LayerCount - 1; i >= 0; i--)
                {
                    axMapControlVisual.AddLayer(axMapControl2.get_Layer(i));
                }

                EnableLabelsForAllLayers();

                // 同步当前视图范围
                if (axMapControl2.LayerCount > 0)
                {
                    axMapControlVisual.Extent = axMapControl2.Extent;
                }

                axMapControlVisual.ActiveView.Refresh();
                axMapControlVisual.Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show("同步演示视图失败: " + ex.Message);
            }
        }

        private void EnableLabelsForAllLayers()
        {
            for (int i = 0; i < axMapControlVisual.LayerCount; i++)
            {
                IGeoFeatureLayer gfl = axMapControlVisual.get_Layer(i) as IGeoFeatureLayer;
                if (gfl == null) continue;

                string[] labelFields = { "名称", "项目名称", "Name", "TITLE" };
                string targetField = "";
                if (gfl.FeatureClass == null) continue;
                for (int j = 0; j < gfl.FeatureClass.Fields.FieldCount; j++)
                {
                    string fName = gfl.FeatureClass.Fields.get_Field(j).Name;
                    foreach (var lf in labelFields) if (fName.Equals(lf, StringComparison.OrdinalIgnoreCase)) { targetField = fName; break; }
                    if (!string.IsNullOrEmpty(targetField)) break;
                }

                if (!string.IsNullOrEmpty(targetField))
                {
                    gfl.DisplayAnnotation = true;
                    ILabelEngineLayerProperties engineProps = new LabelEngineLayerPropertiesClass { Expression = "[" + targetField + "]" };
                    ITextSymbol textSym = new TextSymbolClass { Size = 8 };
                    stdole.IFontDisp font = (stdole.IFontDisp)new stdole.StdFontClass { Name = "微软雅黑" };
                    textSym.Font = font;
                    engineProps.Symbol = textSym;
                    gfl.AnnotationProperties.Clear();
                    gfl.AnnotationProperties.Add(engineProps as IAnnotateLayerProperties);
                }
            }
        }

        // --- 演示模式交互 ---

        private void BtnBackToPro_Click(object sender, EventArgs e) => tabControl1.SelectedIndex = 0;

        private void BtnVisualPan_Click(object sender, EventArgs e)
        {
            // 使用内置漫游工具，操作手感与主界面完全一致
            axMapControlVisual.CurrentTool = new ESRI.ArcGIS.Controls.ControlsMapPanToolClass();
        }

        private void BtnVisualPointer_Click(object sender, EventArgs e)
        {
            // 恢复默认指针，允许点击查询
            axMapControlVisual.CurrentTool = null;
        }

        private void BtnVisualZoomIn_Click(object sender, EventArgs e)
        {
            IEnvelope env = axMapControlVisual.Extent;
            env.Expand(0.5, 0.5, true);
            axMapControlVisual.Extent = env;
            axMapControlVisual.ActiveView.Refresh();
        }

        private void BtnVisualZoomOut_Click(object sender, EventArgs e)
        {
            IEnvelope env = axMapControlVisual.Extent;
            env.Expand(2.0, 2.0, true);
            axMapControlVisual.Extent = env;
            axMapControlVisual.ActiveView.Refresh();
        }

        private void BtnVisualFull_Click(object sender, EventArgs e)
        {
            axMapControlVisual.Extent = axMapControlVisual.FullExtent;
            axMapControlVisual.ActiveView.Refresh();
        }

        private void BtnVisualSync_Click(object sender, EventArgs e)
        {
            SyncToVisualMode();
            MessageBox.Show("图层同步完成！");
        }

        private void BtnVisualSearch_Click(object sender, EventArgs e)
        {
            string keyword = txtVisualSearch.Text.Trim();
            if (string.IsNullOrEmpty(keyword)) return;
            axMapControlVisual.Map.ClearSelection();

            bool found = false;
            for (int i = 0; i < axMapControlVisual.LayerCount; i++)
            {
                IFeatureLayer fl = axMapControlVisual.get_Layer(i) as IFeatureLayer;
                if (fl == null) continue;

                string targetField = "";
                string[] pFields = { "名称", "项目名称", "Name", "TITLE" };
                foreach (var f in pFields) if (fl.FeatureClass.Fields.FindField(f) != -1) { targetField = f; break; }
                if (string.IsNullOrEmpty(targetField)) continue;

                IQueryFilter qf = new QueryFilterClass { WhereClause = $"{targetField} LIKE '%{keyword}%'" };
                IFeatureCursor cursor = fl.Search(qf, true);
                IFeature feature = cursor.NextFeature();

                if (feature != null)
                {
                    if (feature.Shape.GeometryType == esriGeometryType.esriGeometryPoint)
                    {
                        IPoint pt = feature.Shape as IPoint;
                        IEnvelope env = new EnvelopeClass { SpatialReference = axMapControlVisual.Map.SpatialReference };
                        env.XMin = pt.X - 0.075; env.XMax = pt.X + 0.075;
                        env.YMin = pt.Y - 0.075; env.YMax = pt.Y + 0.075;
                        axMapControlVisual.Extent = env;
                    }
                    else
                    {
                        IEnvelope env = feature.Shape.Envelope; env.Expand(1.5, 1.5, true);
                        axMapControlVisual.Extent = env;
                    }

                    axMapControlVisual.Map.SelectFeature(fl, feature);
                    axMapControlVisual.ActiveView.PartialRefresh(esriViewDrawPhase.esriViewGeography, null, null);
                    axMapControlVisual.ActiveView.PartialRefresh(esriViewDrawPhase.esriViewGeoSelection, null, null);
                    found = true;
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(cursor);
                    break;
                }
                System.Runtime.InteropServices.Marshal.ReleaseComObject(cursor);
            }
            if (!found) MessageBox.Show("未找到匹配项", "提示");
        }

        private void AxMapControlVisual_OnMouseDown(object sender, IMapControlEvents2_OnMouseDownEvent e)
        {
            // 仅在左键点击 且 没有启用任何特殊工具（即处于选择/箭头状态）时触发查询
            if (e.button != 1 || axMapControlVisual.CurrentTool != null) return;
            IActiveView av = axMapControlVisual.ActiveView;
            IPoint pt = av.ScreenDisplay.DisplayTransformation.ToMapPoint(e.x, e.y);
            double tol = 5 * av.ScreenDisplay.DisplayTransformation.Resolution;
            IEnvelope env = pt.Envelope; env.Expand(tol, tol, false);

            axMapControlVisual.Map.SelectByShape(env, null, false);
            axMapControlVisual.ActiveView.PartialRefresh(esriViewDrawPhase.esriViewGeoSelection, null, null);

            IEnumFeature enumFeat = axMapControlVisual.Map.FeatureSelection as IEnumFeature;
            enumFeat.Reset();
            IFeature feat = enumFeat.Next();
            if (feat != null) new FormICHDetails(feat).ShowDialog();
        }

        private IFeatureLayer _districtLayerForHeatmap;

        private void BtnDynamicHeatmap_Click(object sender, EventArgs e)
        {
            try
            {
                // 1. Find Data Layers (Point & Polygon)
                _heatmapLayer = null;
                _districtLayerForHeatmap = null;
                _timeField = "";

                for (int i = 0; i < axMapControlVisual.LayerCount; i++)
                {
                    IFeatureLayer fl = axMapControlVisual.get_Layer(i) as IFeatureLayer;
                    if (fl == null) continue;

                    if (fl.FeatureClass.ShapeType == esriGeometryType.esriGeometryPoint)
                    {
                        // Check fields
                        int idx = fl.FeatureClass.Fields.FindField("公布时间");
                        if (idx != -1)
                        {
                            _heatmapLayer = fl;
                            _timeField = "公布时间";
                        }
                    }
                    else if (fl.FeatureClass.ShapeType == esriGeometryType.esriGeometryPolygon)
                    {
                        if (fl.Name.ToLower().Contains("shiqu") || fl.Name.Contains("市区") || fl.Name.Contains("行政"))
                        {
                            _districtLayerForHeatmap = fl;
                        }
                    }
                }
                
                // Fallback for polygon
                if (_districtLayerForHeatmap == null)
                {
                     for (int i = 0; i < axMapControlVisual.LayerCount; i++)
                     {
                        IFeatureLayer fl = axMapControlVisual.get_Layer(i) as IFeatureLayer;
                        if (fl != null && fl.FeatureClass.ShapeType == esriGeometryType.esriGeometryPolygon)
                        {
                            _districtLayerForHeatmap = fl;
                            break;
                        }
                     }
                }

                if (_heatmapLayer == null || _districtLayerForHeatmap == null)
                {
                    MessageBox.Show("启动失败：需要同时存在[含公布时间的点图层]和[行政区面图层]。", "条件不足");
                    return;
                }

                // [Critical Fix] Clear any existing filter to ensure we get the FULL time range
                IFeatureLayerDefinition layerDef = _heatmapLayer as IFeatureLayerDefinition;
                if (layerDef != null) layerDef.DefinitionExpression = "";

                // 2. Statistics on Years
                List<int> years = new List<int>();
                IFeatureCursor cursor = _heatmapLayer.Search(null, true);
                IFeature feature = cursor.NextFeature();
                int idxTime = _heatmapLayer.FeatureClass.Fields.FindField(_timeField);

                while (feature != null)
                {
                    object val = feature.get_Value(idxTime);
                    if (val != null && val != DBNull.Value)
                    {
                        string s = val.ToString();
                        Match m = Regex.Match(s, @"\d{4}");
                        if (m.Success)
                        {
                            if (int.TryParse(m.Value, out int y)) years.Add(y);
                        }
                    }
                    feature = cursor.NextFeature();
                }
                System.Runtime.InteropServices.Marshal.ReleaseComObject(cursor);

                if (years.Count == 0)
                {
                    MessageBox.Show("未从数据中提取到有效的年份信息。", "数据异常");
                    return;
                }

                int minYear = years.Min();
                int maxYear = years.Max();

                // 3. Setup UI - [Fix] Force Panel Expansion to show Slider
                panelVisualHeader.Height = 100; // Expand to reveal slider
                trackBarTime.Top = 50;          // Ensure position
                labelTimeDisplay.Top = 50;
                
                trackBarTime.Visible = true;
                labelTimeDisplay.Visible = true;
                trackBarTime.BringToFront();    // Ensure on top
                
                trackBarTime.Minimum = minYear;
                trackBarTime.Maximum = maxYear;
                trackBarTime.Value = minYear;
                labelTimeDisplay.Text = $"年份: {minYear}";

                // 4. Initial Render
                UpdateHeatmapFilter(minYear);
                
                MessageBox.Show($"时空热力演变已启动。\n年份范围: {minYear} - {maxYear}\n系统将基于各年度点位分布，动态渲染行政区热力色。", "就绪");
            }
            catch (Exception ex)
            {
                MessageBox.Show("启动时空热力图失败: " + ex.Message);
            }
        }

        private void TrackBarTime_Scroll(object sender, EventArgs e)
        {
            if (_heatmapLayer == null) return;
            int currentYear = trackBarTime.Value;
            labelTimeDisplay.Text = $"年份: {currentYear}";
            UpdateHeatmapFilter(currentYear);
        }

        private void UpdateHeatmapFilter(int year)
        {
            try
            {
                // 1. Update Time Filter on Point Layer
                IGeoFeatureLayer geoLayer = _heatmapLayer as IGeoFeatureLayer;
                if (geoLayer == null) return;

                int idx = geoLayer.FeatureClass.Fields.FindField(_timeField);
                if (idx == -1) return;
                
                IField field = geoLayer.FeatureClass.Fields.get_Field(idx);
                string query = "";

                // Robust SQL construction based on Field Type
                if (field.Type == esriFieldType.esriFieldTypeString)
                {
                    // For Strings: Field < 'Value'
                    // Using square brackets [] for field name is safer for GDBs, but raw name is standard for Shapefiles in basic queries.
                    // We'll try to just properly quote the value.
                    // To support "2006年", "2006.5" etc, string comparison usually works locally.
                    query = $"{_timeField} < '{year + 1}'";
                }
                else if (field.Type == esriFieldType.esriFieldTypeInteger || 
                         field.Type == esriFieldType.esriFieldTypeSmallInteger ||
                         field.Type == esriFieldType.esriFieldTypeDouble ||
                         field.Type == esriFieldType.esriFieldTypeSingle)
                {
                    // For Numbers: Field < Value (No Quotes)
                    query = $"{_timeField} < {year + 1}";
                }
                else if (field.Type == esriFieldType.esriFieldTypeDate)
                {
                    // For Dates: Field < date (Complex, assume string format for now or skip)
                    // Trying generic string format often works in personal GDB but fails in Shapefile
                    // Let's try standard format
                    query = $"{_timeField} < date '{year + 1}-01-01 00:00:00'";
                }
                
                IFeatureLayerDefinition layerDef = geoLayer as IFeatureLayerDefinition;
                layerDef.DefinitionExpression = query;
                
                // [Enhanced] Show Total Count in Label
                IQueryFilter qf = new QueryFilterClass { WhereClause = query };
                int totalCount = geoLayer.FeatureClass.FeatureCount(qf);
                labelTimeDisplay.Text = $"年份: {year} (累计: {totalCount} 项)"; // Immediate feedback

                // 2. Trigger Regional Statistical Heatmap (Choropleth)
                // This will count the VISIBLE points (due to our filter update in Helper) and color the polygons
                if (_districtLayerForHeatmap != null)
                {
                    // Pass false to suppress the MessageBox during slider interaction
                    _visualHelper.CreateSpatialStatisticHeatmap(_heatmapLayer, _districtLayerForHeatmap, false);
                }
                
                axMapControlVisual.ActiveView.Refresh();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("UpdateHeatmapFilter Error: " + ex.Message);
            }
        }
    }
}
