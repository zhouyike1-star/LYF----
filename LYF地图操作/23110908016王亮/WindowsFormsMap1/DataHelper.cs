using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using ESRI.ArcGIS.Geodatabase;
using ESRI.ArcGIS.DataSourcesGDB;
using ESRI.ArcGIS.DataSourcesFile;
using ESRI.ArcGIS.Geometry;
using ESRI.ArcGIS.Carto;
using ESRI.ArcGIS.esriSystem;

namespace WindowsFormsMap1
{
    public class DataHelper
    {
        /// <summary>
        /// [重载] 数据入库：直接使用现有 FeatureClass (避免文件锁问题)
        /// </summary>
        public static string SlimDataToGDB(IFeatureClass sourceFeatureClass, string targetGdbDir, string targetGdbName)
        {
            try
            {
                if (sourceFeatureClass == null) return "源要素类无效";

                // 1. 创建/打开工作空间
                IWorkspace targetWorkspace = CreateOrOpenFileGDB(targetGdbDir, targetGdbName);
                if (targetWorkspace == null) return "无法创建或打开目标数据库";

                // 2. 构建过滤条件 (提取山东省)
                IQueryFilter queryFilter = new QueryFilterClass();
                // 检查是否有“省”字段，如果没有则全量导出
                if (sourceFeatureClass.Fields.FindField("省") != -1)
                    queryFilter.WhereClause = "省 = '山东省'";
                else 
                    queryFilter.WhereClause = ""; // 全量

                // 3. 定位目标要素类名称
                string className = (sourceFeatureClass as IDataset).Name + "_SD";
                // 移除可能的扩展名
                if (className.Contains(".")) className = className.Split('.')[0] + "_SD";

                // 如果已存在，则删除
                if ((targetWorkspace as IWorkspace2).get_NameExists(esriDatasetType.esriDTFeatureClass, className))
                {
                    try {
                        IDataset dataset = (targetWorkspace as IFeatureWorkspace).OpenFeatureClass(className) as IDataset;
                        if (dataset.CanDelete()) dataset.Delete();
                    } catch { /* 忽略删除失败，可能是被占用，尝试换个名字 */ 
                         className += "_" + DateTime.Now.Second;
                    }
                }

                // 4. 执行数据导出
                ExportFilteredFeatures(sourceFeatureClass, targetWorkspace as IFeatureWorkspace, className, queryFilter);

                return $"成功！数据已入库至：{targetGdbName}/{className}";
            }
            catch (Exception ex)
            {
                return "入库失败: " + ex.Message;
            }
        }

        // 保留旧方法以兼容 (可选)
        public static string SlimDataToGDB(string sourceShpPath, string targetGdbDir, string targetGdbName)
        {
             IFeatureClass fc = OpenShapefile(sourceShpPath);
             return SlimDataToGDB(fc, targetGdbDir, targetGdbName);
        }

        private static IWorkspace CreateOrOpenFileGDB(string path, string name)
        {
            IWorkspaceFactory workspaceFactory = new FileGDBWorkspaceFactoryClass();
            string fullPath = System.IO.Path.Combine(path, name);

            if (!System.IO.Directory.Exists(fullPath))
            {
                IWorkspaceName workspaceName = workspaceFactory.Create(path, name, null, 0);
                IName nameObj = (IName)workspaceName;
                return (IWorkspace)nameObj.Open();
            }
            else
            {
                return workspaceFactory.OpenFromFile(fullPath, 0);
            }
        }

        private static IFeatureClass OpenShapefile(string fullPath)
        {
            string dir = System.IO.Path.GetDirectoryName(fullPath);
            string file = System.IO.Path.GetFileName(fullPath);

            IWorkspaceFactory workspaceFactory = new ShapefileWorkspaceFactoryClass();
            IFeatureWorkspace featureWorkspace = (IFeatureWorkspace)workspaceFactory.OpenFromFile(dir, 0);
            return featureWorkspace.OpenFeatureClass(file);
        }

        private static void ExportFilteredFeatures(IFeatureClass sourceFC, IFeatureWorkspace targetFW, string newName, IQueryFilter filter)
        {
            // 创建目标要素类 (结构同源)
            IFields targetFields = CloneFields(sourceFC.Fields);

            // [新增] 检查并增加“类别”字段
            if (sourceFC.FindField("类别") == -1)
            {
                IFieldsEdit fieldsEdit = (IFieldsEdit)targetFields;
                IField categoryField = new FieldClass();
                IFieldEdit fieldEdit = (IFieldEdit)categoryField;
                fieldEdit.Name_2 = "类别";
                fieldEdit.Type_2 = esriFieldType.esriFieldTypeString;
                fieldEdit.Length_2 = 50;
                fieldEdit.AliasName_2 = "项目类别";
                fieldsEdit.AddField(categoryField);
            }

            // [Fix 2] E_FAIL 修复: 重构 GeometryDef
            // 原因: Shapefile 的 GeometryDef 缺少 Geodatabase 需要的 GridIndex 信息
            // 解决方案: 创建全新的 GeometryDef，而不是修改克隆来的
            string shapeFieldName = sourceFC.ShapeFieldName;
            int shapeFieldIdx = targetFields.FindField(shapeFieldName);
            
            if (shapeFieldIdx != -1)
            {
                IField shapeField = targetFields.get_Field(shapeFieldIdx);
                IGeometryDefEdit geoDefEdit = new GeometryDefClass();
                
                // 1. 复制几何类型
                IGeometryDef sourceGeoDef = sourceFC.Fields.get_Field(sourceFC.FindField(shapeFieldName)).GeometryDef;
                geoDefEdit.GeometryType_2 = sourceGeoDef.GeometryType;
                
                // 2. 复制/设置空间参考
                if (sourceGeoDef.SpatialReference != null)
                {
                    geoDefEdit.SpatialReference_2 = sourceGeoDef.SpatialReference;
                }

                // 3. 设置 Grid (FileGDB 关键)
                geoDefEdit.GridCount_2 = 1;
                geoDefEdit.set_GridSize(0, 0.0); // 0.0 让 ArcEngine 自动计算合适的 GridSize
                geoDefEdit.HasM_2 = false;
                geoDefEdit.HasZ_2 = false;

                // 将新的 GeometryDef 赋给 Shape 字段
                ((IFieldEdit)shapeField).GeometryDef_2 = geoDefEdit;
            }

            // 安全检查：字段名合法性 (FileGDB 不支持以数字开头或含特殊字符)
            // 这里做一个简化处理，实际应遍历所有字段
            
            IFeatureClass targetFC = targetFW.CreateFeatureClass(newName, targetFields, sourceFC.CLSID, sourceFC.EXTCLSID, sourceFC.FeatureType, sourceFC.ShapeFieldName, "");

            // 循环插入 (瘦身核心)
            IFeatureCursor sourceCursor = sourceFC.Search(filter, true);
            IFeature sourceFeature = sourceCursor.NextFeature();

            IFeatureCursor insertCursor = targetFC.Insert(true);

            // [Fix Performance] 使用 ComReleaser 管理或者每 1000 条 Flush 一次
            int count = 0;
            while (sourceFeature != null)
            {
                IFeatureBuffer featureBuffer = targetFC.CreateFeatureBuffer();
                // 拷贝属性
                for (int i = 0; i < sourceFeature.Fields.FieldCount; i++)
                {
                    IField sourceField = sourceFeature.Fields.get_Field(i);
                    if (sourceField.Type == esriFieldType.esriFieldTypeGeometry)
                    {
                        featureBuffer.Shape = sourceFeature.ShapeCopy; // 使用 ShapeCopy 更安全
                    }
                    else if (sourceField.Type != esriFieldType.esriFieldTypeOID)
                    {
                        int targetIdx = targetFC.FindField(sourceField.Name);
                        if (targetIdx != -1)
                        {
                            IField targetField = targetFC.Fields.get_Field(targetIdx);
                            if (targetField.Editable)
                            {
                                featureBuffer.set_Value(targetIdx, sourceFeature.get_Value(i));
                            }
                        }
                    }
                }
                insertCursor.InsertFeature(featureBuffer);
                sourceFeature = sourceCursor.NextFeature();
                
                count++;
                if (count % 1000 == 0) insertCursor.Flush(); // 定期 Flush 避免内存溢出
            }
            insertCursor.Flush();

            System.Runtime.InteropServices.Marshal.ReleaseComObject(sourceCursor);
            System.Runtime.InteropServices.Marshal.ReleaseComObject(insertCursor);
        }

        private static IFields CloneFields(IFields sourceFields)
        {
            // 简单克隆逻辑，实际生产中需更严谨处理 GeometryDef
            return (IFields)((IClone)sourceFields).Clone();
        }

        /// <summary>
        /// 离散化处理：将重叠在同一点的要素稍微散开，呈环状分布
        /// </summary>
        /// <param name="featureClass">要处理的要素类</param>
        /// <param name="offsetDistanceDegree">偏移距离(经纬度单位,0.0005约为50米)</param>
        public static string DisplaceDuplicatePoints(IFeatureClass featureClass, double offsetDistanceDegree = 0.0008)
        {
            try
            {
                if (featureClass == null) return "要素类为空";
                if (featureClass.ShapeType != esriGeometryType.esriGeometryPoint) return "仅支持点要素类";

                // 1. 扫描所有位置并将要素分组
                Dictionary<string, List<int>> locationMap = new Dictionary<string, List<int>>();
                IFeatureCursor cursor = featureClass.Search(null, false);
                IFeature feature;

                while ((feature = cursor.NextFeature()) != null)
                {
                    IPoint pt = feature.Shape as IPoint;
                    if (pt == null || pt.IsEmpty) continue;

                    // 使用6位小数精度作为判定重叠的标准
                    string key = $"{pt.X:F6},{pt.Y:F6}";
                    if (!locationMap.ContainsKey(key))
                        locationMap[key] = new List<int>();

                    locationMap[key].Add(feature.OID);
                }
                System.Runtime.InteropServices.Marshal.ReleaseComObject(cursor);

                // 2. 识别需要处理的重叠组
                var duplicateGroups = locationMap.Values.Where(ids => ids.Count > 1).ToList();
                if (duplicateGroups.Count == 0) return "未发现重叠点,无需处理。";

                int processedCount = 0;
                // 3. 执行偏移处理
                foreach (var ids in duplicateGroups)
                {
                    // 每一个组，让第一个保持不动(中心)，其余环绕
                    for (int i = 1; i < ids.Count; i++)
                    {
                        IFeature featToMove = featureClass.GetFeature(ids[i]);
                        IPoint pt = featToMove.ShapeCopy as IPoint;

                        // 计算环绕位置: 均匀分布在圆周上
                        double angle = (2 * Math.PI / (ids.Count - 1)) * (i - 1);
                        pt.X += offsetDistanceDegree * Math.Cos(angle);
                        pt.Y += offsetDistanceDegree * Math.Sin(angle);

                        featToMove.Shape = pt;
                        featToMove.Store();
                        processedCount++;
                    }
                }

                return $"离散化完成！处理了 {duplicateGroups.Count} 处重叠, 移动了 {processedCount} 个要素。";
            }
            catch (Exception ex)
            {
                return "离散化失败: " + ex.Message;
            }
        }
        /// <summary>
        /// 获取图层中满足条件的要素数量
        /// </summary>
        /// <param name="featureClass">目标要素类</param>
        /// <param name="whereClause">查询条件 (例如 "City = '济南市'")，为空则统计所有</param>
        /// <returns>要素数量</returns>
        public static int GetFeatureCount(IFeatureClass featureClass, string whereClause = "")
        {
            if (featureClass == null) return 0;
            try
            {
                IQueryFilter queryFilter = new QueryFilterClass();
                queryFilter.WhereClause = whereClause;
                return featureClass.FeatureCount(queryFilter);
            }
            catch
            {
                return 0;
            }
        }
        /// <summary>
        /// 检查图层健康状况 (字段是否存在)
        /// </summary>
        public static string CheckLayerHealth(ILayer layer, List<string> requiredFields)
        {
            if (layer == null) return "图层为空";
            IFeatureLayer fl = layer as IFeatureLayer;
            if (fl == null) return "非要素图层，跳过检查";
            if (fl.FeatureClass == null) return "要素类无效 (FeatureClass is null)";

            List<string> missingFields = new List<string>();
            foreach (var field in requiredFields)
            {
                if (fl.FeatureClass.Fields.FindField(field) == -1)
                {
                    // 尝试匹配别名或不区分大小写
                    bool found = false;
                    for(int i=0; i<fl.FeatureClass.Fields.FieldCount; i++)
                    {
                        if (fl.FeatureClass.Fields.get_Field(i).Name.Equals(field, StringComparison.OrdinalIgnoreCase))
                        {
                            found = true; 
                            break;
                        }
                    }
                    if(!found) missingFields.Add(field);
                }
            }

            if (missingFields.Count > 0)
                return "未通过: 缺失关键字段 [" + string.Join(", ", missingFields) + "]";
            else
                return "通过 (字段完整)";
        }

        /// <summary>
        /// 获取图层所有要素的名称列表 (仅返回名称用于简单展示或统计)
        /// </summary>
        public static List<string> GetAllFeatures(IFeatureLayer layer, string nameField)
        {
            List<string> names = new List<string>();
            if (layer == null || layer.FeatureClass == null) return names;

            int idx = layer.FeatureClass.Fields.FindField(nameField);
            if (idx == -1) return names;

            IFeatureCursor cursor = layer.FeatureClass.Search(null, true);
            IFeature feature;
            while ((feature = cursor.NextFeature()) != null)
            {
                object val = feature.get_Value(idx);
                if (val != null) names.Add(val.ToString());
            }
            System.Runtime.InteropServices.Marshal.ReleaseComObject(cursor);
            return names;
        }

    }
}
