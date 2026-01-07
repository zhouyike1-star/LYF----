using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ESRI.ArcGIS.Carto;
using ESRI.ArcGIS.Controls;
using ESRI.ArcGIS.Geodatabase;
using ESRI.ArcGIS.Geometry;
using ESRI.ArcGIS.Display;
using System.Windows.Forms;

namespace WindowsFormsMap1
{
    /// <summary>
    /// 组员 E (沉浸式演示) 核心逻辑辅助类
    /// 负责: 热力图生成、漫游飞行、多媒体展示逻辑
    /// </summary>
    public class VisualHelper
    {
        private AxMapControl _mapControl;
        private Timer _flyTimer;

        public VisualHelper(AxMapControl mapControl)
        {
            _mapControl = mapControl;
        }

        #region 热力图 (Heatmap)
        
        /// <summary>
        /// 生成核密度热力图 (Kernel Density)
        /// 注意：需要 Spatial Analyst 许可，如果不可用则降级为点密度渲染
        /// </summary>
        /// <summary>
        /// 生成基于面状的空间统计热力图 (Choropleth Map)
        /// 统计每个多边形(districtLayer)内部包含的点(pointLayer)数量，并进行分层设色
        /// </summary>
        /// <param name="showMessage">是否显示完成提示框 (拖动滑动条时应设为 false)</param>
        public void CreateSpatialStatisticHeatmap(IFeatureLayer pointLayer, IFeatureLayer districtLayer, bool showMessage = true)
        {
            try
            {
                if (pointLayer == null || districtLayer == null)
                {
                    if (showMessage) MessageBox.Show("图层无效，无法生成热力图。\n需要同时存在[点图层]和[面图层(shiqu)]");
                    return;
                }

                IFeatureClass pointClass = pointLayer.FeatureClass;
                IFeatureClass districtClass = districtLayer.FeatureClass;
                
                // [Cache] 缓存用于智能导览
                _districtLayer = districtLayer;

                // 1. 确保面图层有一个用于存储统计值的字段 "HeatCount"
                string countFieldName = "HeatCount";
                int countFieldIndex = districtClass.Fields.FindField(countFieldName);

                // 如果字段不存在，则添加字段
                if (countFieldIndex == -1)
                {
                    // 注意：添加字段需要排他锁，如果正在编辑可能会失败
                    try 
                    {
                        IField newField = new FieldClass();
                        IFieldEdit fieldEdit = newField as IFieldEdit;
                        fieldEdit.Name_2 = countFieldName;
                        fieldEdit.Type_2 = esriFieldType.esriFieldTypeInteger;
                        districtClass.AddField(newField);
                        countFieldIndex = districtClass.Fields.FindField(countFieldName);
                    }
                    catch 
                    {
                        if (showMessage) MessageBox.Show("无法自动添加统计字段 'HeatCount'，可能是图层正在被使用。\n尝试直接使用现有的数值字段进行渲染。");
                        // 如果失败，尝试找一个替代字段或者中止
                        // 这里为了演示健壮性，若失败则跳过统计步骤，直接渲染（假设已有数据）
                    }
                }

                // 2. 执行空间统计：Point in Polygon
                // 只有当成功获取到字段索引时才进行计算
                if (countFieldIndex != -1)
                {
                    // 获取点图层的空间参考
                    IGeoDataset geoDataset = pointClass as IGeoDataset;
                    ISpatialReference pointSR = geoDataset.SpatialReference;

                    IFeatureCursor cursor = districtClass.Update(null, false);
                    IFeature districtFeature = cursor.NextFeature();

                    int maxCountFound = 0; // 用于调试：记录最大统计值
                    int processedFeatures = 0;

                    while (districtFeature != null)
                    {
                        // 处理几何投影：确保查询几何与点图层空间参考一致
                        IGeometry queryGeometry = districtFeature.Shape;
                        if (pointSR != null)
                        {
                            queryGeometry.Project(pointSR);
                        }

                        // 构造空间过滤器
                        ISpatialFilter spatialFilter = new SpatialFilterClass();
                        spatialFilter.Geometry = queryGeometry;
                        spatialFilter.SpatialRel = esriSpatialRelEnum.esriSpatialRelContains; 
                        spatialFilter.GeometryField = pointClass.ShapeFieldName;
                        
                        // [Fix] Ensure we respect the Time Filter (DefinitionQuery) on the point layer
                        IFeatureLayerDefinition def = pointLayer as IFeatureLayerDefinition;
                        if (def != null && !string.IsNullOrEmpty(def.DefinitionExpression))
                        {
                            spatialFilter.WhereClause = def.DefinitionExpression;
                        }

                        // 查询点图层
                        int count = pointClass.FeatureCount(spatialFilter);

                        if (count > maxCountFound) maxCountFound = count;

                        // 更新统计值
                        districtFeature.set_Value(countFieldIndex, count);
                        cursor.UpdateFeature(districtFeature);

                        districtFeature = cursor.NextFeature();
                        processedFeatures++;
                    }
                    // 释放游标
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(cursor);
                    
                    // 调试信息：如果最大值仍为0，说明空间关系没匹配上
                    if (maxCountFound == 0 && showMessage)
                    {
                        MessageBox.Show($"警告：已遍历 {processedFeatures} 个区域，但所有区域内统计到的点数量均为 0。\n" +
                                        "可能原因：\n" +
                                        "1. 坐标系不匹配 (已尝试投影修正)\n" +
                                        "2. 图层并未发生地理重叠\n" +
                                        "3. 空间索引可能需要重建", "统计结果异常");
                    }
                }

                // 3. 应用分层设色渲染 (Class Breaks Renderer)
                IClassBreaksRenderer renderer = new ClassBreaksRendererClass();
                renderer.Field = countFieldName; 
                renderer.BreakCount = 5;
                renderer.MinimumBreak = 0;

                // 创建色带 (黄色 -> 红色)
                IAlgorithmicColorRamp colorRamp = new AlgorithmicColorRampClass();
                colorRamp.Algorithm = esriColorRampAlgorithm.esriHSVAlgorithm;
                colorRamp.FromColor = new RgbColorClass { Red = 255, Green = 255, Blue = 200 }; // 浅黄
                colorRamp.ToColor = new RgbColorClass { Red = 200, Green = 0, Blue = 0 };       // 深红
                colorRamp.Size = 5;
                bool ok;
                colorRamp.CreateRamp(out ok);

                // 寻找最大值以设定断点
                // 优化：针对非遗数据可能较少的情况，降低阈值
                double[] breaks = { 0, 2, 5, 10, 20, 9999 }; 
                
                // [Opt] If needed, verify max value from stats to adjust breaks dynamically
                
                for (int i = 0; i < 5; i++)
                {
                    renderer.set_Break(i, breaks[i+1]);
                    renderer.set_Label(i, $"{breaks[i]} - {breaks[i+1]}");

                    ISimpleFillSymbol fillSym = new SimpleFillSymbolClass();
                    fillSym.Style = esriSimpleFillStyle.esriSFSSolid;
                    fillSym.Color = colorRamp.get_Color(i);
                    fillSym.Outline = new SimpleLineSymbolClass { Width = 0.5 }; // 保留细边框

                    renderer.set_Symbol(i, fillSym as ISymbol);
                }

                // 应用到面图层
                IGeoFeatureLayer geoDistrictLayer = districtLayer as IGeoFeatureLayer;
                geoDistrictLayer.Renderer = renderer as IFeatureRenderer;
                
                // [Fix] 确保点图层在最上层，防止被填充的面遮挡
                // MoveLayerTo 需要两个 int 参数: (FromIndex, ToIndex)
                int pointLayerIndex = -1;
                for (int k = 0; k < _mapControl.LayerCount; k++)
                {
                    if (_mapControl.get_Layer(k) == pointLayer)
                    {
                        pointLayerIndex = k;
                        break;
                    }
                }
                
                if (pointLayerIndex != -1 && pointLayerIndex != 0)
                {
                    _mapControl.MoveLayerTo(pointLayerIndex, 0);
                }

                // 刷新
                _mapControl.ActiveView.Refresh();
                _mapControl.ActiveView.ContentsChanged();

                if (showMessage) MessageBox.Show($"热力统计完成！\n已统计各区域内的点位数量，并按[黄色->红色]分级渲染。\n点图层已置顶显示。", "完成");
            }
            catch (Exception ex)
            {
                MessageBox.Show("空间统计热力图生成失败: " + ex.Message);
            }
        }
        
        #endregion

        #region 演示自动机 (Demo Automaton)

        private double _stepX;
        private double _stepY;
        private IEnvelope _fullExtent;
        private IFeatureLayer _districtLayer; // 缓存面图层用于HUD
        
        // [New] 记录当前漫游方向，用于边界检测
        private FormRoamingSetting.RoamDirection _currentDirection;

        private ITextElement _hudElement; // 复用 HUD 元素

        /// <summary>
        /// 启动自动漫游飞行 (平滑平移)
        /// </summary>
        /// <param name="direction">漫游方向</param>
        /// <param name="durationSeconds">遍历全图大概需要的秒数，默认 30秒</param>
        public void StartFlyOver(FormRoamingSetting.RoamDirection direction, double durationSeconds = 30.0)
        {
            if (_mapControl.LayerCount == 0) return;

            _currentDirection = direction;

            // [Auto-Detect] 如果尚未设置 _districtLayer，尝试自动查找
            if (_districtLayer == null)
            {
                for (int i = 0; i < _mapControl.LayerCount; i++)
                {
                    IFeatureLayer fl = _mapControl.get_Layer(i) as IFeatureLayer;
                    if (fl != null && fl.FeatureClass.ShapeType == esriGeometryType.esriGeometryPolygon)
                    {
                        if (fl.Name.ToLower().Contains("shiqu") || fl.Name.Contains("市区"))
                        {
                            _districtLayer = fl;
                            break;
                        }
                        if (_districtLayer == null) _districtLayer = fl;
                    }
                }
            }

            // 1. 获取全图范围 & 当前视图大小
            _fullExtent = _mapControl.FullExtent;
            IEnvelope currentEnv = _mapControl.Extent;
            double width = currentEnv.Width;
            double height = currentEnv.Height;

            // 2. 根据方向计算起始视图 (StartEnv) 和 步长 (Step)
            // 降低刷新频率防崩溃 & 闪烁优化: 50ms - 100ms
            double interval = 50; 
            double totalSteps = (durationSeconds * 1000) / interval;
            
            _stepX = 0;
            _stepY = 0;

            IEnvelope startEnv = new EnvelopeClass();
            // 先默认设置为当前全图中心
            startEnv.PutCoords(_fullExtent.XMin, _fullExtent.YMin, _fullExtent.XMax, _fullExtent.YMax);

            // 重新计算起始位置
            switch (direction)
            {
                case FormRoamingSetting.RoamDirection.LeftToRight:
                    // 起点：最左侧，垂直居中
                    {
                        double centerY = (_fullExtent.YMin + _fullExtent.YMax) / 2.0;
                        startEnv.PutCoords(_fullExtent.XMin, centerY - height/2.0, _fullExtent.XMin + width, centerY + height/2.0);
                        
                        double totalDist = _fullExtent.Width - width;
                        if (totalDist < 0) totalDist = 0;
                        _stepX = totalDist / totalSteps;
                    }
                    break;

                case FormRoamingSetting.RoamDirection.RightToLeft:
                    // 起点：最右侧
                    {
                        double centerY = (_fullExtent.YMin + _fullExtent.YMax) / 2.0;
                        startEnv.PutCoords(_fullExtent.XMax - width, centerY - height/2.0, _fullExtent.XMax, centerY + height/2.0);

                        double totalDist = _fullExtent.Width - width;
                        if (totalDist < 0) totalDist = 0;
                        _stepX = -(totalDist / totalSteps); // 向左负移动
                    }
                    break;

                case FormRoamingSetting.RoamDirection.TopToBottom:
                    // 起点：最上侧，水平居中
                    {
                        double centerX = (_fullExtent.XMin + _fullExtent.XMax) / 2.0;
                        startEnv.PutCoords(centerX - width/2.0, _fullExtent.YMax - height, centerX + width/2.0, _fullExtent.YMax);

                        double totalDist = _fullExtent.Height - height;
                        if (totalDist < 0) totalDist = 0;
                        _stepY = -(totalDist / totalSteps); // 向下负移动
                    }
                    break;

                case FormRoamingSetting.RoamDirection.BottomToTop:
                    // 起点：最下侧
                    {
                        double centerX = (_fullExtent.XMin + _fullExtent.XMax) / 2.0;
                        startEnv.PutCoords(centerX - width/2.0, _fullExtent.YMin, centerX + width/2.0, _fullExtent.YMin + height);

                        double totalDist = _fullExtent.Height - height;
                        if (totalDist < 0) totalDist = 0;
                        _stepY = totalDist / totalSteps; // 向上正移动
                    }
                    break;
            }

            // 应用起始视图
            _mapControl.Extent = startEnv;
            // 初始化 HUD 元素
            CreateHUDElelment();
            _mapControl.ActiveView.Refresh(); // 初始刷一次

            // 4. 启动定时器
            if (_flyTimer == null)
            {
                _flyTimer = new Timer();
                _flyTimer.Tick += FlyTimer_Tick;
            }
            _flyTimer.Interval = (int)interval; 
            _flyTimer.Start();
        }

        public void StopFlyOver()
        {
            if (_flyTimer != null) _flyTimer.Stop();
            
            // 安全清除 HUD
            try
            {
                // 直接使用 DeleteAllElements 防止 DeleteElement 因对象不存在而报错 (E_FAIL)
                _mapControl.ActiveView.GraphicsContainer.DeleteAllElements();
                _hudElement = null; 
                _mapControl.ActiveView.Refresh();
            }
            catch 
            {
                // 忽略清理时的任何 COM 错误
            }
        }

        private void CreateHUDElelment()
        {
            try
            {
                IGraphicsContainer gc = _mapControl.ActiveView.GraphicsContainer;
                // 先清理旧的
                gc.DeleteAllElements(); 

                _hudElement = new TextElementClass();
                _hudElement.Text = "智能导览系统启动中...";

                // 样式
                IFormattedTextSymbol textSym = new TextSymbolClass();
                textSym.Size = 18;
                stdole.IFontDisp font = (stdole.IFontDisp)new stdole.StdFontClass { Name = "微软雅黑", Bold = true };
                textSym.Font = font;
                textSym.Color = new RgbColorClass { Red = 0, Green = 0, Blue = 139 };

                IBalloonCallout callout = new BalloonCalloutClass();
                callout.Style = esriBalloonCalloutStyle.esriBCSRoundedRectangle;
                IColor backColor = new RgbColorClass { Red = 255, Green = 255, Blue = 255 };
                backColor.Transparency = 200;
                
                ISimpleFillSymbol fill = new SimpleFillSymbolClass();
                fill.Color = backColor;
                fill.Style = esriSimpleFillStyle.esriSFSSolid;
                callout.Symbol = fill;
                textSym.Background = callout as ITextBackground;

                _hudElement.Symbol = textSym;
                
                // 添加到容器
                _mapControl.ActiveView.GraphicsContainer.AddElement(_hudElement as IElement, 0);
            }
            catch { }
        }

        private void FlyTimer_Tick(object sender, EventArgs e)
        {
            try 
            {
                IEnvelope currentEnv = _mapControl.Extent;
                bool isEnd = false;

                // 边界检测
                switch (_currentDirection)
                {
                    case FormRoamingSetting.RoamDirection.LeftToRight:
                        if (currentEnv.XMax >= _fullExtent.XMax) isEnd = true;
                        break;
                    case FormRoamingSetting.RoamDirection.RightToLeft:
                        if (currentEnv.XMin <= _fullExtent.XMin) isEnd = true;
                        break;
                    case FormRoamingSetting.RoamDirection.TopToBottom:
                        if (currentEnv.YMin <= _fullExtent.YMin) isEnd = true;
                        break;
                    case FormRoamingSetting.RoamDirection.BottomToTop:
                        if (currentEnv.YMax >= _fullExtent.YMax) isEnd = true;
                        break;
                }
                
                if (isEnd)
                {
                    StopFlyOver();
                    MessageBox.Show("漫游演示结束。", "提示");
                    return;
                }

                currentEnv.Offset(_stepX, _stepY);
                
                // [Optimization] 
                // 设置 Extent 会自动触发 Refresh(esriViewGeography)
                // 因此这里不需要手动调用 PartialRefresh(Geography)
                // 也无需 PartialRefresh(Graphics)，因为 HUD 是 Element，随地图移动需要刷新
                // 但如果想让 HUD 保持屏幕相对静止或更新内容，需要重绘 Graphics
                // 标注闪烁通常是因为短时间内多次全刷造成的
                
                _mapControl.Extent = currentEnv;
                
                UpdateSmartHUD(currentEnv);

                // 仅刷新 Graphics 层以更新 HUD 文字
                // 标注层(Annotation/Label)通常随Geography层绘制，Extent改变会自动重绘
                _mapControl.ActiveView.PartialRefresh(esriViewDrawPhase.esriViewGraphics, null, null);
            }
            catch { }
        }

        private void UpdateSmartHUD(IEnvelope currentEnv)
        {
            if (_districtLayer == null || _hudElement == null) return;
            
            try
            {
                // HUD 位置：固定在视图上方中间
                IPoint p = new PointClass();
                p.PutCoords(currentEnv.XMin + currentEnv.Width * 0.5, currentEnv.YMax - currentEnv.Height * 0.1); 
                (_hudElement as IElement).Geometry = p;

                // 空间查询
                IPoint center = new PointClass();
                center.PutCoords((currentEnv.XMin + currentEnv.XMax) / 2.0, (currentEnv.YMin + currentEnv.YMax) / 2.0);

                ISpatialFilter sf = new SpatialFilterClass();
                sf.Geometry = center;
                sf.SpatialRel = esriSpatialRelEnum.esriSpatialRelWithin;
                sf.GeometryField = _districtLayer.FeatureClass.ShapeFieldName;

                IFeatureCursor cursor = _districtLayer.Search(sf, true);
                IFeature feature = cursor.NextFeature();

                if (feature != null)
                {
                    int idxName = feature.Fields.FindField("名称");
                    if (idxName == -1) idxName = feature.Fields.FindField("Name");
                    int idxCount = feature.Fields.FindField("HeatCount");

                    string nameVal = (idxName != -1) ? feature.get_Value(idxName).ToString() : "未知区域";
                    string countVal = (idxCount != -1) ? feature.get_Value(idxCount).ToString() : "N/A";

                    _hudElement.Text = $"智能导览: 当前正在飞越 [{nameVal}]\n该区域非遗数量: {countVal} 项";
                }
                else
                {
                     _hudElement.Text = "智能导览: 漫游中...";
                }
                
                _mapControl.ActiveView.GraphicsContainer.UpdateElement(_hudElement as IElement);

                System.Runtime.InteropServices.Marshal.ReleaseComObject(cursor);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(sf);
            }
            catch { }
        }

        #endregion
    }
}
