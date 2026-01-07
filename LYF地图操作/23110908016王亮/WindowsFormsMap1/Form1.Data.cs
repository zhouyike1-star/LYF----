using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using ESRI.ArcGIS.Carto;
using ESRI.ArcGIS.Geodatabase;

namespace WindowsFormsMap1
{
    public partial class Form1
    {
        // 成员 D：数据中台 (Data Fabric)
        
        public void InitDataModule()
        {
            // 启动时的初始检查 (写入日志文件)
            string report = RunDataHealthCheck();
            string logPath = System.IO.Path.Combine(Application.StartupPath, "DataHealthLog.txt");
            System.IO.File.WriteAllText(logPath, report);
        }

        /// <summary>
        /// [核心逻辑] 运行数据自检并返回报告字符串 (支持实时调用)
        /// </summary>
        public string RunDataHealthCheck()
        {
            StringBuilder logObj = new StringBuilder();
            logObj.AppendLine($"[数据模块体检报告] - 时间: {DateTime.Now.ToLongTimeString()}");
            logObj.AppendLine("--------------------------------------------------");

            try
            {
                if (axMapControl2 == null) return "错误: 地图控件未初始化";

                int count = axMapControl2.LayerCount;
                logObj.AppendLine($"当前图层数量: {count}");

                if (count == 0)
                {
                    logObj.AppendLine("\r\n[提示] 地图无图层。请先加载 Shapefile 数据（如拖入或打开Mxd）。");
                    return logObj.ToString();
                }

                // 遍历所有图层进行检查
                logObj.AppendLine("\r\n--- 图层健康检测 ---");
                bool foundHeritage = false;

                for (int i = 0; i < count; i++)
                {
                    ILayer layer = axMapControl2.get_Layer(i);
                    if (layer is IFeatureLayer)
                    {
                        foundHeritage = true;
                        IFeatureLayer fl = layer as IFeatureLayer;
                        
                        // 1. 字段检查
                        string checkResult = DataHelper.CheckLayerHealth(fl, new List<string> { "名称", "公布时间", "Time" }); // 简略检查
                        
                        // 2. 坐标系检查
                        string srName = "Unknown";
                        if (fl is IGeoDataset)
                        {
                            ESRI.ArcGIS.Geometry.ISpatialReference sr = (fl as IGeoDataset).SpatialReference;
                            if (sr != null) srName = sr.Name;
                        }
                        string srStatus = (srName.Contains("WGS") || srName.Contains("2000") || srName.Contains("4326")) ? "✅" : "⚠️";

                        // 3. 路径获取
                        string path = "N/A";
                        try 
                        { 
                            if (fl.FeatureClass != null)
                                path = (fl.FeatureClass as IDataset).Workspace.PathName; 
                        } 
                        catch { }

                        logObj.AppendLine($"\r\n>>> 图层[{i}]: {layer.Name}");
                        // logObj.AppendLine($"   状态: {checkResult}"); // [User Request] 移除关键字段状态显示
                        logObj.AppendLine($"   坐标: {srStatus} {srName}");
                        logObj.AppendLine($"   位置: {path}");
                    }
                }

                if (!foundHeritage)
                {
                    logObj.AppendLine("未找到“非遗”关键字图层。");
                    if (axMapControl2.LayerCount > 0)
                        logObj.AppendLine("当前第一图层名称: " + axMapControl2.get_Layer(0).Name);
                }
            }
            catch (Exception ex)
            {
                logObj.AppendLine($"检测过程异常: {ex.Message}");
            }
            return logObj.ToString();
        }
        
        /// <summary>
        /// [API] 获取所有非遗项目要素的名称列表 (供组员B/C使用)
        /// </summary>
        public List<string> GetAllHeritageFeatures()
        {
             // 1. 查找非遗图层
             IFeatureLayer targetLayer = null;
             for (int i = 0; i < axMapControl2.LayerCount; i++)
             {
                 ILayer l = axMapControl2.get_Layer(i);
                 if (l is IFeatureLayer && (l.Name.Contains("非遗") || l.Name.Contains("项目") || l.Name.Contains("ICH")))
                 {
                     targetLayer = l as IFeatureLayer;
                     break;
                 }
             }
             
             if(targetLayer == null) return new List<string>();
             
             // 2. 获取名称列表
             return DataHelper.GetAllFeatures(targetLayer, "名称");
        }

        /// <summary>
        /// [UI] 弹出数据健康报告
        /// </summary>
        public void CheckDataHealthUI()
        {
            // [Fix] 实时运行检查，而不是读取旧文件
            // 解决 "图层数为0" 的问题，因为用户可能在启动后才加载数据
            string currentReport = RunDataHealthCheck();
            MessageBox.Show(currentReport, "数据体检 (实时)", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// [高级功能] 数据入库: 将当前非遗 Shapefile 导入 GDB
        /// </summary>
        public void ImportDataUI()
        {
            // 1. 自动查找非遗图层
            IFeatureLayer targetLayer = null;
            for (int i = 0; i < axMapControl2.LayerCount; i++)
            {
                ILayer l = axMapControl2.get_Layer(i);
                if (l is IFeatureLayer && (l.Name.Contains("非遗") || l.Name.Contains("项目") || l.Name.Contains("名录") || l.Name.Contains("ICH")))
                {
                    targetLayer = l as IFeatureLayer;
                    break;
                }
            }

            if (targetLayer == null)
            {
                MessageBox.Show("未找到'非遗'相关图层，无法执行入库。", "停止", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 2. 选择目标位置 (直接入库，跳过源路径获取，避免逻辑复杂化)
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            dialog.Description = "请选择或新建 FileGDB (.gdb) 所在的文件夹";
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                string targetDir = dialog.SelectedPath;
                string gdbName = "ShandongICH.gdb"; 
                
                // 如果用户选的是 .gdb 文件夹本身，做一下智能修正
                if (targetDir.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
                {
                    gdbName = System.IO.Path.GetFileName(targetDir);
                    targetDir = System.IO.Path.GetDirectoryName(targetDir);
                }

                // 4. 执行转换 (直接传递 FeatureClass)
                this.Cursor = Cursors.WaitCursor;
                string result = DataHelper.SlimDataToGDB(targetLayer.FeatureClass, targetDir, gdbName);
                this.Cursor = Cursors.Default;

                MessageBox.Show(result, "入库结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>
        /// [高级功能] 导出统计报表 (.csv)
        /// </summary>
        public void ExportStatsUI()
        {
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "CSV 文件 (*.csv)|*.csv";
            sfd.FileName = "山东省非遗统计报表_" + DateTime.Now.ToString("yyyyMMdd") + ".csv";
            
            if (sfd.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    // 1. 准备图层
                    IFeatureLayer layer = GetHeritageLayer();
                    if (layer == null) throw new Exception("未找到非遗图层");

                    StringBuilder sb = new StringBuilder();
                    // [Upgrade v2] 删除空列，新增“最多类别”
                    sb.AppendLine("城市,非遗总数(个),最多类别(Top Category),类别数量,代表性项目"); 

                    string[] cities = new string[] { 
                        "济南市", "青岛市", "淄博市", "枣庄市", "东营市", "烟台市", "潍坊市", "济宁市", 
                        "泰安市", "威海市", "日照市", "临沂市", "德州市", "聊城市", "滨州市", "菏泽市" 
                    };

                    // 预先获取字段名
                    string realCityField = "市"; 
                     string[] cityKeys = { "市", "City", "Name", "NAME", "地市", "所属地区", "申报地区", "申报地区或" };
                     for (int i = 0; i < layer.FeatureClass.Fields.FieldCount; i++) {
                         string fName = layer.FeatureClass.Fields.get_Field(i).Name;
                         foreach (string k in cityKeys) if (fName.Equals(k, StringComparison.OrdinalIgnoreCase)) { realCityField = fName; break; }
                     }
                    
                    // 获取名称和类别字段
                    string nameField = "名称";
                    int nIdx = layer.FeatureClass.Fields.FindField("名称");
                    
                    string catField = "类别";
                    int cIdx = layer.FeatureClass.Fields.FindField("类别");
                    if (cIdx == -1) cIdx = layer.FeatureClass.Fields.FindField("Category");

                    foreach (var city in cities)
                    {
                        string shortName = city.Replace("市", "");
                        string baseWhere = $"({realCityField} = '{city}' OR {realCityField} LIKE '%{shortName}%')";

                        // 1. 总数
                        int total = DataHelper.GetFeatureCount(layer.FeatureClass, baseWhere);
                        
                        // 2. 计算最多类别 (Top Category)
                        string topCat = "-";
                        int topCatCount = 0;
                        string repName = "无";

                        // 为了统计类别，我们需要获取该城市下所有 feature 的类别值
                        // 注意：如果数据量巨大，这里会有性能问题。但在市级统计这个规模一般没问题。
                        if (cIdx != -1)
                        {
                            ESRI.ArcGIS.Geodatabase.IQueryFilter qf = new ESRI.ArcGIS.Geodatabase.QueryFilterClass();
                            qf.WhereClause = baseWhere;
                            // 仅索取需要的字段，稍微优化性能
                            qf.SubFields = $"{layer.FeatureClass.Fields.get_Field(cIdx).Name},{layer.FeatureClass.Fields.get_Field(nIdx).Name}";
                            
                            ESRI.ArcGIS.Geodatabase.IFeatureCursor c = layer.FeatureClass.Search(qf, true);
                            ESRI.ArcGIS.Geodatabase.IFeature f;
                            
                            Dictionary<string, int> catStats = new Dictionary<string, int>();
                            while((f = c.NextFeature()) != null)
                            {
                                // 统计类别
                                object oCat = f.get_Value(cIdx);
                                string sCat = (oCat == null) ? "未知" : oCat.ToString();
                                if(!catStats.ContainsKey(sCat)) catStats[sCat] = 0;
                                catStats[sCat]++;
                                
                                // 顺便抓一个代表项目名
                                if (repName == "无")
                                {
                                    object oName = f.get_Value(nIdx);
                                    if(oName != null) repName = oName.ToString();
                                }
                            }
                            System.Runtime.InteropServices.Marshal.ReleaseComObject(c);

                            // 排序找最大
                            if (catStats.Count > 0)
                            {
                                var maxItem = catStats.OrderByDescending(x => x.Value).First();
                                topCat = maxItem.Key;
                                topCatCount = maxItem.Value;
                            }
                        }

                        // [Fix] 用户要求: 数量为0时显示 "-"
                        string sTotal = total == 0 ? "-" : total.ToString();
                        string sCatCount = topCatCount == 0 ? "-" : topCatCount.ToString();

                        sb.AppendLine($"{city},{sTotal},{topCat},{sCatCount},{repName}");
                    }

                    // 使用 GB2312 编码以防止 Excel 打开乱码
                    System.IO.File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.GetEncoding("GB2312"));
                    
                    MessageBox.Show($"导出成功！\n保存路径: {sfd.FileName}", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("导出失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>
        /// [API] 获取指定城市的非遗项目数量 (基于真实数据字段查询)
        /// </summary>
        /// <param name="cityName">城市名称</param>
        /// <param name="year">当前滑块年份</param>
        /// <summary>
        /// [Helper] 查找非遗相关图层
        /// </summary>
        private IFeatureLayer GetHeritageLayer()
        {
            if (axMapControl2 == null) return null;
            for (int i = 0; i < axMapControl2.LayerCount; i++)
            {
                ILayer layer = axMapControl2.get_Layer(i);
                if (layer is IFeatureLayer && layer.Visible)
                {
                    string n = layer.Name;
                    if (n.Contains("名录") || n.Contains("项目") || n.Contains("非遗") || n.Contains("ICH"))
                    {
                        return layer as IFeatureLayer;
                    }
                }
            }
            // Fallback
            if (axMapControl2.LayerCount > 0 && axMapControl2.get_Layer(0) is IFeatureLayer)
                return axMapControl2.get_Layer(0) as IFeatureLayer;
            return null;
        }

        public int GetCountByCity(string cityName, int year)
        {
            try
            {
                IFeatureLayer targetLayer = GetHeritageLayer();
                if (targetLayer == null) return 0;

                return QueryCount(targetLayer, cityName, year);
            }
            catch { return 0; }
        }

        // 提取核心查询逻辑，方便复用
        private int QueryCount(IFeatureLayer targetLayer, string cityName, int year)
        {
                 IFields fields = targetLayer.FeatureClass.Fields;
 
                 // 2. 匹配城市字段
                 string realCityField = "";
                 string[] cityKeys = { "市", "City", "Name", "NAME", "地市", "所属地区", "申报地区", "申报地区或" };
                 for (int i = 0; i < fields.FieldCount; i++)
                 {
                     string fName = fields.get_Field(i).Name;
                     foreach (string k in cityKeys) if (fName.Equals(k, StringComparison.OrdinalIgnoreCase)) { realCityField = fName; break; }
                     if (!string.IsNullOrEmpty(realCityField)) break;
                 }
 
                 // 3. 匹配时间字段
                 string realTimeField = "";
                 bool isNumeric = false;
                 // 优先找 XML 中确认的 "公布时间"
                 int tIdx = fields.FindField("公布时间");
                 if (tIdx == -1) tIdx = fields.FindField("Time");
                 if (tIdx == -1) tIdx = fields.FindField("Year");
                 
                 if (tIdx != -1)
                 {
                     IField f = fields.get_Field(tIdx);
                     realTimeField = f.Name;
                     isNumeric = (f.Type != esriFieldType.esriFieldTypeString && f.Type != esriFieldType.esriFieldTypeDate);
                 }
 
                 // 4. 构建 SQL
                 string baseWhere = "";
                 string shortName = cityName.Replace("市", "");
                 if (!string.IsNullOrEmpty(realCityField))
                 {
                     baseWhere = $"({realCityField} = '{cityName}' OR {realCityField} LIKE '%{shortName}%')";
                 }
 
                 if (string.IsNullOrEmpty(realTimeField)) return DataHelper.GetFeatureCount(targetLayer.FeatureClass, baseWhere);
 
                 int batch = 0;
                 if (year >= 2021) batch = 5;
                 else if (year >= 2014) batch = 4;
                 else if (year >= 2011) batch = 3;
                 else if (year >= 2008) batch = 2;
                 else if (year >= 2006) batch = 1;
 
                 string timeClause = "";
                 if (isNumeric)
                     timeClause = $"(({realTimeField} >= 1900 AND {realTimeField} <= {year}) OR ({realTimeField} > 0 AND {realTimeField} <= {batch} AND {realTimeField} < 20))";
                 else
                     timeClause = $"({realTimeField} LIKE '%{year}%' OR {realTimeField} LIKE '%{batch}%')";
 
                 string finalWhere = string.IsNullOrEmpty(baseWhere) ? timeClause : $"{baseWhere} AND {timeClause}";
                 return DataHelper.GetFeatureCount(targetLayer.FeatureClass, finalWhere);
        }
    }
}
