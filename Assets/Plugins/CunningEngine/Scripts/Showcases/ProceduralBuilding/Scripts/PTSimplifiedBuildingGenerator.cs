using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using ProceduralToolkit;
using ProceduralToolkit.Buildings;
using System.Linq;

namespace DevStuffs
{
    /// <summary>
    /// 基于ProceduralToolkit实现的简化建筑生成器
    /// 作为连接ProceduralToolkit和ProceduralBuilding的桥梁
    /// </summary>
    public class PTSimplifiedBuildingGenerator
    {
        // 建筑配置
        public BuildingGenerator.Config buildingConfig;
        
        // 建筑生成器
        private BuildingGenerator buildingGenerator;
        
        // 立面规划者
        public SimpleFacadePlanner facadePlanner;
        
        // 立面构造器
        private SimpleFacadeConstructor facadeConstructor;
        
        // 屋顶规划者
        private SimpleRoofPlanner roofPlanner;
        
        // 屋顶构造器
        private SimpleRoofConstructor roofConstructor;
        
        // 新增颜色配置
        public Color shopWallColor = new Color(0.8f, 0.75f, 0.7f); // Example color
        public Color shopCeilingColor = new Color(0.7f, 0.65f, 0.6f);
        public Color upperWallColor = new Color(0.85f, 0.85f, 0.85f);
        public Color upperRoofColor = new Color(0.6f, 0.4f, 0.3f);
        public Color windowFrameColor = Color.gray;
        public Color doorColor = new Color(0.4f, 0.3f, 0.2f);
        public Color glassColor = new Color(0.5f, 0.7f, 0.9f, 0.5f);
        public Color basementColor = new Color(0.5f, 0.5f, 0.5f); // 底座默认灰色
        public float basementHeight = 0.5f; // 底座默认高度
        public float shopHeight = 4.5f; // 商铺高度
        
        /// <summary>
        /// 初始化建筑生成器
        /// </summary>
        public PTSimplifiedBuildingGenerator()
        {
            buildingGenerator = new BuildingGenerator();
            facadePlanner = new SimpleFacadePlanner();
            facadeConstructor = new SimpleFacadeConstructor();
            roofPlanner = new SimpleRoofPlanner();
            roofConstructor = new SimpleRoofConstructor();
            
            buildingGenerator.SetFacadePlanner(facadePlanner);
            buildingGenerator.SetFacadeConstructor(facadeConstructor);
            buildingGenerator.SetRoofPlanner(roofPlanner);
            buildingGenerator.SetRoofConstructor(roofConstructor);
            
            // 新增：把facadePlanner传给facadeConstructor
            facadeConstructor.facadePlanner = facadePlanner;
            // 新增：传递generatorContext
            facadeConstructor.generatorContext = this;
            facadePlanner.generatorContext = this; // For consistency, though not directly used yet
            roofConstructor.generatorContext = this; // Added this line
            
            // 默认配置
            buildingConfig = new BuildingGenerator.Config
            {
                floors = 5,
                entranceInterval = 12,
                hasAttic = true,
                roofConfig = new RoofConfig
                {
                    type = RoofType.Flat,
                    thickness = 0.2f,
                    overhang = 0.2f,
                },
                palette = new Palette
                {
                    wallColor = Color.white,
                    frameColor = Color.gray,
                    glassColor = new Color(0.3f, 0.3f, 0.3f, 0.5f),
                    roofColor = Color.gray
                }
            };
            
            // 设置新的颜色配置
            facadePlanner.shopWallColor = this.shopWallColor;
            facadePlanner.windowFrameColor = this.windowFrameColor;
            facadePlanner.doorColor = this.doorColor;
            facadePlanner.glassColor = this.glassColor;
            facadePlanner.upperWallColor = this.upperWallColor;
        }
        
        /// <summary>
        /// 生成建筑
        /// </summary>
        /// <param name="points">建筑平面多边形顶点</param>
        /// <param name="parent">建筑父对象</param>
        /// <param name="floorCount">楼层数量</param>
        /// <param name="floorHeight">楼层高度</param>
        /// <param name="useFixedHeight">是否使用固定楼层高度</param>
        /// <param name="fixedHeight">固定楼层高度</param>
        /// <param name="stageHeightMultiplier">楼层高度乘数</param>
        /// <param name="roofType">屋顶类型</param>
        /// <param name="hasShops">是否有底层商铺</param>
        /// <param name="shopHeight">商铺高度</param>
        /// <param name="windowsColor">窗户颜色</param>
        /// <param name="wallsColor">墙体颜色</param>
        /// <param name="roofColor">屋顶颜色</param>
        /// <param name="shopColor">商铺颜色</param>
        /// <param name="basementHeight">底座高度</param>
        /// <returns>生成的建筑根节点</returns>
        public Transform GenerateBuilding(
            List<Vector3> points,
            Transform parent,
            int floorCount = 5,
            float floorHeight = 3.5f,
            bool useFixedHeight = false,
            float fixedHeight = 3.5f,
            float stageHeightMultiplier = 1.0f,
            RoofType roofType = RoofType.Flat,
            bool hasShops = true,
            float shopHeight = 4.5f,
            Color windowsColor = default,
            Color wallsColor = default,
            Color roofColor = default,
            Color shopColor = default,
            float basementHeight = 0.5f)
        {
            // 设置底座高度
            this.basementHeight = basementHeight;
            
            // 转换3D点为2D平面多边形
            List<Vector2> foundationPolygon = new List<Vector2>();
            foreach (Vector3 point in points)
            {
                // 确保使用局部坐标而非世界坐标
                Vector3 localPoint = parent.InverseTransformPoint(point);
                foundationPolygon.Add(new Vector2(localPoint.x, localPoint.z));
            }
            
            // 确保多边形顶点是顺时针排序的（Unity中网格面朝上需要顺时针顶点）
            if (!IsClockwise(foundationPolygon))
            {
                foundationPolygon.Reverse();
            }
            
            // 更新配置
            buildingConfig.floors = floorCount;
            buildingConfig.roofConfig.type = roofType;
            
            // 设置颜色
            if (windowsColor != default)
                buildingConfig.palette.glassColor = windowsColor;
            else
                buildingConfig.palette.glassColor = new Color(0.5f, 0.7f, 0.9f, 0.5f);
            
            if (wallsColor != default)
                buildingConfig.palette.wallColor = wallsColor;
            else
                buildingConfig.palette.wallColor = new Color(0.85f, 0.85f, 0.85f);
                
            if (roofColor != default)
                buildingConfig.palette.roofColor = roofColor;
            else
                buildingConfig.palette.roofColor = new Color(0.6f, 0.4f, 0.3f);
                
            // 设置商铺颜色
            if (shopColor == default)
                shopColor = new Color(0.7f, 0.7f, 0.7f);
            
            // 设置高度配置
            facadePlanner.floorHeight = useFixedHeight ? fixedHeight : floorHeight;
            facadePlanner.heightMultiplier = stageHeightMultiplier;
            facadePlanner.hasShops = hasShops;
            facadePlanner.shopHeight = shopHeight;
            facadePlanner.shopColor = shopColor;
            
            // 创建建筑父对象
            GameObject buildingObject = new GameObject("PTSimplifiedBuilding");
            buildingObject.transform.parent = parent;
            buildingObject.transform.localPosition = Vector3.zero;
            
            // 生成建筑
            buildingGenerator.Generate(foundationPolygon, buildingConfig, buildingObject.transform);
            
            return buildingObject.transform;
        }
        
        /// <summary>
        /// 检查多边形顶点是否是顺时针排序的
        /// </summary>
        private bool IsClockwise(List<Vector2> vertices)
        {
            float sum = 0;
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector2 current = vertices[i];
                Vector2 next = vertices[(i + 1) % vertices.Count];
                sum += (next.x - current.x) * (next.y + current.y);
            }
            return sum > 0;
        }
    }
    
    /// <summary>
    /// 简单的立面规划器
    /// </summary>
    public class SimpleFacadePlanner : IFacadePlanner
    {
        // 楼层高度
        public float floorHeight = 3.5f;
        public PTSimplifiedBuildingGenerator generatorContext;
        
        // 高度乘数
        public float heightMultiplier = 1.0f;
        
        // 是否有底层商铺
        public bool hasShops = true;
        
        // 商铺高度
        public float shopHeight = 4.5f;
        
        public Color shopColor = Color.white; // Original, can be deprecated or used as general tint for shop area
        
        // 商铺窗户宽度
        public float shopWindowWidth = 3.0f;
        
        // 商铺窗户高度
        public float shopWindowHeight = 3.2f;
        
        // 门宽度
        public float doorWidth = 1.8f;
        
        // 门高度
        public float doorHeight = 3.2f;
        
        // 新颜色字段
        public Color shopWallColor;
        public Color windowFrameColor;
        public Color doorColor;
        public Color glassColor;
        public Color upperWallColor;
        
        public List<ILayout> Plan(List<Vector2> foundationPolygon, BuildingGenerator.Config config)
        {
            // 创建立面布局
            var facadeLayouts = new List<ILayout>();
            
            // 计算有效高度
            float actualFloorHeight = floorHeight * heightMultiplier;
            
            // 计算总高度（考虑商铺层）
            float buildingHeight;
            // 修改逻辑：如果有商铺，普通楼层数减1（因为商铺算第一层）
            int normalFloors = config.floors;
            
            if (hasShops)
            {
                buildingHeight = (normalFloors * actualFloorHeight) + shopHeight;
            }
            else
            {
                buildingHeight = normalFloors * actualFloorHeight;
            }
            
            // 创建简单的立面布局
            for (int i = 0; i < foundationPolygon.Count; i++)
            {
                Vector2 start = foundationPolygon[i];
                Vector2 end = foundationPolygon[(i + 1) % foundationPolygon.Count];
                
                // 计算立面尺寸
                Vector2 wallDirection = end - start;
                float wallLength = wallDirection.magnitude;
                
                // 创建立面布局
                ILayout layout;
                
                if (hasShops)
                {
                    // 使用复合布局包含商铺和普通窗户区域
                    var verticalLayout = new VerticalLayout
                {
                    origin = Vector2.zero,
                    height = buildingHeight,
                    width = wallLength
                };
                    
                    // 添加商铺区域 - 最底层
                    var shopLayoutInstance = new ShopLayout // Renamed to avoid conflict with class name
                    {
                        origin = Vector2.zero, 
                        height = shopHeight,
                        width = wallLength,
                        shopWindowWidth = shopWindowWidth,
                        shopWindowHeight = shopWindowHeight,
                        doorWidth = doorWidth,
                        doorHeight = doorHeight,
                        shopColor = shopColor
                    };
                    
                    // 如果墙面足够长，至少放置一个门
                    if (wallLength >= doorWidth * 1.5f)
                    {
                        // 每隔一定距离放置一个门
                        int doorCount = Mathf.FloorToInt(wallLength / config.entranceInterval);
                        doorCount = Mathf.Max(1, doorCount); // 至少一个门
                        
                        float doorSpacing = wallLength / doorCount;
                        for (int d = 0; d < doorCount; d++)
                        {
                            float doorPosition = (d + 0.5f) * doorSpacing - doorWidth / 2;
                            if (doorPosition >= 0 && doorPosition + doorWidth <= wallLength)
                            {
                                shopLayoutInstance.doorPositions.Add(new Vector2(doorPosition, 0));
                            }
                        }
                    }
                    
                    // 添加上部窗户区域 - 确保使用正确的楼层数量
                    var windowLayoutInstance = new VerticalLayout // Renamed to avoid conflict
                    {
                        origin = new Vector2(0, shopHeight),
                        height = buildingHeight - shopHeight,
                        width = wallLength
                    };
                    
                    // 存储当前的楼层数量，用于后续窗户生成
                    windowLayoutInstance.GetUserData()["floorCount"] = normalFloors; // 使用普通楼层数
                    
                    // 组合布局
                    verticalLayout.AddChild(shopLayoutInstance);
                    verticalLayout.AddChild(windowLayoutInstance);
                    
                    layout = verticalLayout;
                }
                else
                {
                    // 不需要商铺，只创建普通窗户布局
                    var simpleLayout = new VerticalLayout
                    {
                        origin = Vector2.zero,
                        height = buildingHeight,
                        width = wallLength
                    };
                    
                    // 存储当前的楼层数量，用于后续窗户生成
                    simpleLayout.GetUserData()["floorCount"] = normalFloors; // 使用普通楼层数
                    
                    layout = simpleLayout;
                }
                
                facadeLayouts.Add(layout);
            }
            
            return facadeLayouts;
        }
    }
    
    /// <summary>
    /// 商铺布局类，用于底层商铺
    /// </summary>
    public class ShopLayout : ILayout
    {
        public Vector2 origin { get; set; }
        public float width { get; set; }
        public float height { get; set; }
        
        // 商铺窗户尺寸
        public float shopWindowWidth = 3.0f;
        public float shopWindowHeight = 3.2f;
        
        // 门尺寸
        public float doorWidth = 1.8f;
        public float doorHeight = 3.2f;
        
        // 门的位置列表
        public List<Vector2> doorPositions = new List<Vector2>();
        
        // 商铺颜色
        public Color shopColor = Color.white;
        
        // 实现ILayout接口所需的元素集合
        private readonly List<ILayoutElement> elements = new List<ILayoutElement>();
        
        // 实现Add方法
        public void Add(ILayoutElement element)
        {
            elements.Add(element);
        }
        
        // 实现GetEnumerator方法 (泛型版本)
        public IEnumerator<ILayoutElement> GetEnumerator()
        {
            return elements.GetEnumerator();
        }
        
        // 实现GetEnumerator方法 (非泛型版本)
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
    
    /// <summary>
    /// 扩展垂直布局，添加对子布局的支持
    /// </summary>
    public static class VerticalLayoutExtensions
    {
        // 使用静态字典存储每个VerticalLayout实例的子布局
        private static readonly Dictionary<VerticalLayout, List<ILayout>> childrenMap = 
            new Dictionary<VerticalLayout, List<ILayout>>();
        
        // 使用静态字典存储每个VerticalLayout实例的用户数据
        private static readonly Dictionary<VerticalLayout, Dictionary<string, object>> userDataMap =
            new Dictionary<VerticalLayout, Dictionary<string, object>>();
        
        public static List<ILayout> GetChildren(this VerticalLayout layout)
        {
            // 如果字典中没有这个布局的记录，创建一个新列表
            if (!childrenMap.ContainsKey(layout))
            {
                childrenMap[layout] = new List<ILayout>();
            }
            
            return childrenMap[layout];
        }
        
        public static void AddChild(this VerticalLayout layout, ILayout child)
        {
            // 将子布局添加到布局中
            GetChildren(layout).Add(child);
            
            // 同时使用原生的Add方法添加到布局中
            layout.Add(child);
        }
        
        // 获取或创建userData字典
        public static Dictionary<string, object> GetUserData(this VerticalLayout layout)
        {
            if (!userDataMap.ContainsKey(layout))
            {
                userDataMap[layout] = new Dictionary<string, object>();
            }
            
            return userDataMap[layout];
        }
        
        // 获取userData属性
        public static Dictionary<string, object> userData(this VerticalLayout layout)
        {
            return GetUserData(layout);
        }
    }
    
    /// <summary>
    /// 简单的立面构造器
    /// </summary>
    public class SimpleFacadeConstructor : IFacadeConstructor
    {
        public SimpleFacadePlanner facadePlanner;
        public PTSimplifiedBuildingGenerator generatorContext;

        public void Construct(List<Vector2> foundationPolygon, List<ILayout> facadeLayouts, Transform parent)
        {
            // Pass generatorContext to constructor or set it from PTSimplifiedBuildingGenerator
            if (this.generatorContext == null && facadePlanner != null && facadePlanner.GetType().BaseType != null)
            {
                //有点hacky，后续应该直接从PTSimplifiedBuildingGenerator注入
                 var plannerField = facadePlanner.GetType().GetField("generatorContext"); 
                 if (plannerField != null) 
                    this.generatorContext = (PTSimplifiedBuildingGenerator)plannerField.GetValue(facadePlanner);
            }
            if (this.generatorContext == null) {
                 // Fallback if not found, to prevent nullrefs, though colors will be default.
                 // This indicates an issue with context passing.
                 Debug.LogWarning("PTSimplifiedBuildingGenerator context not found in SimpleFacadeConstructor. Using default colors.");
                 this.generatorContext = new PTSimplifiedBuildingGenerator(); 
            }

            // 创建父级 GameObjects
            GameObject shopsParentGO = new GameObject("ShopsStructure");
            shopsParentGO.transform.SetParent(parent, false);
            MeshFilter shopsMF = shopsParentGO.AddComponent<MeshFilter>();
            MeshRenderer shopsMR = shopsParentGO.AddComponent<MeshRenderer>();
            // TODO: Assign materials to shopsMR

            GameObject upperStructureParentGO = new GameObject("UpperStructure");
            upperStructureParentGO.transform.SetParent(parent, false);
            MeshFilter upperMF = upperStructureParentGO.AddComponent<MeshFilter>();
            MeshRenderer upperMR = upperStructureParentGO.AddComponent<MeshRenderer>();
            // TODO: Assign materials to upperMR
            
            // 创建底座
            if (this.generatorContext.basementHeight > 0)
            {
                CreateBasement(foundationPolygon, parent);
            }
            
            // 将生成的墙壁添加到网格草稿中
            for (int i = 0; i < foundationPolygon.Count; i++)
            {
                Vector2 start = foundationPolygon[i];
                Vector2 end = foundationPolygon[(i + 1) % foundationPolygon.Count];
                
                Vector2 wallDirection = end - start;
                Vector2 wallNormal = new Vector2(-wallDirection.y, wallDirection.x).normalized;
                
                // CreateWallDraft handles parenting its generated segments to shopsParentGO.transform or upperStructureParentGO.transform
                CreateWallDraft(start, end, facadeLayouts[i].height, facadeLayouts[i], wallNormal, shopsParentGO.transform, upperStructureParentGO.transform);
            }
            
            // 创建地面
            var foundationDraft = CreateFoundationDraft(foundationPolygon);
            EnsureMeshIntegrity(foundationDraft);
            // Foundation can remain on the main parent or be its own child
            GameObject foundationGO = new GameObject("Foundation");
            foundationGO.transform.SetParent(parent, false);
            MeshFilter foundationMF = foundationGO.AddComponent<MeshFilter>();
            MeshRenderer foundationMR = foundationGO.AddComponent<MeshRenderer>();
            foundationMF.mesh = foundationDraft.ToMesh();
            Material foundationMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            foundationMat.color = Color.grey;
            foundationMR.material = foundationMat;
        }
        
        /// <summary>
        /// 创建建筑底座
        /// </summary>
        private void CreateBasement(List<Vector2> foundationPolygon, Transform parent)
        {
            GameObject basementGO = new GameObject("Basement");
            basementGO.transform.SetParent(parent, false);
            
            // 创建一个MeshDraft来构建底座网格
            MeshDraft basementDraft = new MeshDraft();
            
            // 获取世界空间下的建筑位置
            Vector3 buildingWorldPos = parent.position;
            
            // 地形嵌入深度（米）
            float terrainEmbedDepth = 2.0f;
            
            // 遍历多边形边缘创建底座侧面
            for (int i = 0; i < foundationPolygon.Count; i++)
            {
                Vector2 start = foundationPolygon[i];
                Vector2 end = foundationPolygon[(i + 1) % foundationPolygon.Count];
                
                // 计算方向和法线
                Vector2 wallDirection = end - start;
                Vector2 wallNormal = new Vector2(-wallDirection.y, wallDirection.x).normalized;
                
                // 转换为世界空间坐标用于射线检测
                Vector3 worldStartBottom = buildingWorldPos + new Vector3(start.x, 0, start.y);
                Vector3 worldEndBottom = buildingWorldPos + new Vector3(end.x, 0, end.y);
                
                // 对底部左右顶点进行射线检测
                float startGroundHeight = -generatorContext.basementHeight;
                float endGroundHeight = -generatorContext.basementHeight;
                
                // 检测起点位置的地面高度
                if (Physics.Raycast(worldStartBottom + Vector3.up * 100f, Vector3.down, out RaycastHit hitStartInfo, 200f))
                {
                    // 计算局部空间中的地面高度，并向下延伸设定的深度
                    startGroundHeight = parent.InverseTransformPoint(hitStartInfo.point).y - terrainEmbedDepth;
                    Debug.DrawLine(worldStartBottom + Vector3.up * 100f, hitStartInfo.point, Color.green, 1.0f);
                    // 显示嵌入点
                    Debug.DrawLine(hitStartInfo.point, hitStartInfo.point + Vector3.down * terrainEmbedDepth, Color.red, 1.0f);
                }
                else
                {
                    // 如果没有检测到地形，使用默认高度并额外延伸
                    startGroundHeight = -generatorContext.basementHeight - terrainEmbedDepth;
                }
                
                // 检测终点位置的地面高度
                if (Physics.Raycast(worldEndBottom + Vector3.up * 100f, Vector3.down, out RaycastHit hitEndInfo, 200f))
                {
                    // 计算局部空间中的地面高度，并向下延伸设定的深度
                    endGroundHeight = parent.InverseTransformPoint(hitEndInfo.point).y - terrainEmbedDepth;
                    Debug.DrawLine(worldEndBottom + Vector3.up * 100f, hitEndInfo.point, Color.green, 1.0f);
                    // 显示嵌入点
                    Debug.DrawLine(hitEndInfo.point, hitEndInfo.point + Vector3.down * terrainEmbedDepth, Color.red, 1.0f);
                }
                else
                {
                    // 如果没有检测到地形，使用默认高度并额外延伸
                    endGroundHeight = -generatorContext.basementHeight - terrainEmbedDepth;
                }
                
                // 创建底座侧面顶点 - 底部使用射线检测的高度
                Vector3 bl = new Vector3(start.x, startGroundHeight, start.y); // 左下 - 使用射线检测高度并向下延伸
                Vector3 br = new Vector3(end.x, endGroundHeight, end.y);       // 右下 - 使用射线检测高度并向下延伸
                Vector3 tl = new Vector3(start.x, 0, start.y);                 // 左上
                Vector3 tr = new Vector3(end.x, 0, end.y);                     // 右上
                
                // 确保法线方向向外
                Vector3 normal3D = new Vector3(wallNormal.x, 0, wallNormal.y);
                
                // 添加侧面四边形
                int vertexCount = basementDraft.vertices.Count;
                basementDraft.vertices.Add(bl);
                basementDraft.vertices.Add(br);
                basementDraft.vertices.Add(tr);
                basementDraft.vertices.Add(tl);
                
                // 添加法线
                for (int j = 0; j < 4; j++)
                {
                    basementDraft.normals.Add(normal3D);
                    basementDraft.colors.Add(generatorContext.basementColor);
                }
                
                // 添加UV坐标
                basementDraft.uv.Add(new Vector2(0, 0));
                basementDraft.uv.Add(new Vector2(1, 0));
                basementDraft.uv.Add(new Vector2(1, 1));
                basementDraft.uv.Add(new Vector2(0, 1));
                
                // 添加三角形索引
                basementDraft.triangles.Add(vertexCount);
                basementDraft.triangles.Add(vertexCount + 1);
                basementDraft.triangles.Add(vertexCount + 2);
                
                basementDraft.triangles.Add(vertexCount);
                basementDraft.triangles.Add(vertexCount + 2);
                basementDraft.triangles.Add(vertexCount + 3);
            }
            
            // 确保网格完整性
            EnsureMeshIntegrity(basementDraft);
            
            // 添加Mesh组件并应用网格
            MeshFilter mf = basementGO.AddComponent<MeshFilter>();
            MeshRenderer mr = basementGO.AddComponent<MeshRenderer>();
            mf.mesh = basementDraft.ToMesh();
            
            // 创建底座材质
            Material basementMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            basementMat.color = generatorContext.basementColor;
            mr.material = basementMat;
            
            // 添加碰撞器以便与地形交互
            basementGO.AddComponent<MeshCollider>().sharedMesh = mf.mesh;
        }
        
        /// <summary>
        /// 创建墙面网格草稿
        /// </summary>
        private MeshDraft CreateWallDraft(Vector2 start, Vector2 end, float height, ILayout layout, Vector2 normal, Transform shopsContainer, Transform upperContainer)
        {
            // This method needs significant refactoring.
            // It should no longer create one giant wall quad for the whole segment.
            // Instead, it should look at the 'layout' (VerticalLayout and its children ShopLayout/WindowLayout)
            // and generate MeshDrafts for each part, then either add them to the correct parent (shopsParent/upperStructureParent)
            // or return a combined MeshDraft for the *current segment* that will be later assigned.
            // For now, let's return a placeholder or null, and focus on the Construct method structure.

            var segmentSpecificDraft = new MeshDraft { name = "FacadeSegment" };
            float currentYOffsetForChildren = 0f; 
            
            // 创建墙面顶点 - 确保正确映射到3D空间
            Vector3 baseBottomLeft = new Vector3(start.x, 0, start.y);
            Vector3 baseBottomRight = new Vector3(end.x, 0, end.y);
            // Vector3 baseTopLeft = new Vector3(start.x, height, start.y); // Height is overall segment height
            // Vector3 baseTopRight = new Vector3(end.x, height, end.y);

            Vector3 segmentDirection3D = (baseBottomRight - baseBottomLeft).normalized;
            Vector3 segmentNormal3D = Vector3.Cross(Vector3.up, segmentDirection3D).normalized;
            Vector2 midPoint2D = (start + end) / 2f;
            Vector2 center2D = Vector2.zero; 
            Vector2 toEdge2D = midPoint2D - center2D;
            if (Vector2.Dot(new Vector2(segmentNormal3D.x, segmentNormal3D.z), toEdge2D) < 0)
            {
                segmentNormal3D = -segmentNormal3D;
            }

            if (layout is VerticalLayout mainVerticalLayout)
            {
                MeshDraft shopsMeshDraft = new MeshDraft(); // Collect all shop parts for this segment
                MeshDraft upperMeshDraft = new MeshDraft(); // Collect all upper parts for this segment
                bool shopPartExists = false;
                bool upperPartExists = false;

                foreach (var childLayout in mainVerticalLayout.GetChildren()) 
                {
                    if (childLayout is ShopLayout shopInstance)
                    {
                        Vector3 shopAreaBottomLeft = baseBottomLeft + Vector3.up * currentYOffsetForChildren;
                        // Create shop background wall
                        AddFacadeQuad(shopsMeshDraft, shopAreaBottomLeft, baseBottomRight + Vector3.up * currentYOffsetForChildren, shopInstance.height, segmentNormal3D, facadePlanner.shopWallColor); 
                        AddShop(shopsMeshDraft, shopAreaBottomLeft, segmentDirection3D, segmentNormal3D, shopInstance);
                        currentYOffsetForChildren += shopInstance.height; 
                        shopPartExists = true;
                    }
                    else if (childLayout is VerticalLayout windowAreaLayout && windowAreaLayout.GetUserData().ContainsKey("floorCount"))
                    {
                        int floorsForThisArea = (int)windowAreaLayout.GetUserData()["floorCount"];
                        float heightOfThisArea = windowAreaLayout.height;
                        Vector3 windowAreaSpecificBottomLeft = baseBottomLeft + Vector3.up * currentYOffsetForChildren;
                        Vector3 windowAreaSpecificBottomRight = baseBottomRight + Vector3.up * currentYOffsetForChildren;
                        
                        // Create upper background wall for this window area
                        AddFacadeQuad(upperMeshDraft, windowAreaSpecificBottomLeft, windowAreaSpecificBottomRight, heightOfThisArea, segmentNormal3D, Color.white); // Placeholder color
                        AddStandardWindows(upperMeshDraft, windowAreaSpecificBottomLeft, windowAreaSpecificBottomRight, 
                                           heightOfThisArea, 0, segmentNormal3D, floorsForThisArea);
                        currentYOffsetForChildren += heightOfThisArea; 
                        upperPartExists = true;
                    }
                }
                
                // If only shops (e.g. a 1-story shop building), but no explicit upper window layout
                if (shopPartExists && !upperPartExists && mainVerticalLayout.GetChildren().Count == 1 && mainVerticalLayout.GetChildren()[0] is ShopLayout)
                {
                    // This case is handled: shopsMeshDraft will contain the shop.
                }
                // If no shops, and the mainVerticalLayout itself defines floors (a building without a shop layer)
                else if (!shopPartExists && mainVerticalLayout.GetUserData().ContainsKey("floorCount"))
                {   
                    int floorsForMainLayout = (int)mainVerticalLayout.GetUserData()["floorCount"];
                    // Create background wall for the entire height
                    AddFacadeQuad(upperMeshDraft, baseBottomLeft, baseBottomRight, height, segmentNormal3D, Color.white); // Placeholder color
                    AddStandardWindows(upperMeshDraft, baseBottomLeft, baseBottomRight, 
                                       height, 0, segmentNormal3D, floorsForMainLayout);
                    upperPartExists = true;
                }

                // Now, add these collected drafts to their respective GameObjects for this facade segment
                if (shopPartExists)
                {
                    var shopSegmentGO = new GameObject("ShopFacadeSegment");
                    shopSegmentGO.transform.SetParent(shopsContainer, false);
                    var mf = shopSegmentGO.AddComponent<MeshFilter>();
                    var mr = shopSegmentGO.AddComponent<MeshRenderer>();
                    EnsureMeshIntegrity(shopsMeshDraft);
                    Material[] shopMats = null;
                    mf.mesh = shopsMeshDraft.ToMeshWithSubMeshes(out shopMats, this.generatorContext); 
                    if (shopMats != null && shopMats.Length > 0 && shopMats[0] != null) { // First material is wall
                        shopMats[0].color = facadePlanner.shopWallColor;
                    }
                    mr.materials = shopMats;

                    // Add ceiling for this shop segment, if it had a ShopLayout
                    if (layout is VerticalLayout vLayout && vLayout.GetChildren().Any(c => c is ShopLayout))
                    {
                        ShopLayout actualShopLayout = vLayout.GetChildren().First(c => c is ShopLayout) as ShopLayout;
                        AddShopCeiling(shopSegmentGO.transform, 
                                       (end-start).magnitude, 
                                       actualShopLayout.height, 
                                       this.generatorContext.shopCeilingColor);
                    }
                }
                if (upperPartExists)
                {
                    var upperSegmentGO = new GameObject("UpperFacadeSegment");
                    upperSegmentGO.transform.SetParent(upperContainer, false);
                    var mf = upperSegmentGO.AddComponent<MeshFilter>();
                    var mr = upperSegmentGO.AddComponent<MeshRenderer>();
                    EnsureMeshIntegrity(upperMeshDraft);
                    Material[] upperMats = null;
                    mf.mesh = upperMeshDraft.ToMeshWithSubMeshes(out upperMats, this.generatorContext);
                     if (upperMats != null && upperMats.Length > 0 && upperMats[0] != null) { // First material is wall
                        upperMats[0].color = facadePlanner.upperWallColor;
                    }
                    mr.materials = upperMats;
                }
                return null; // Handled internally
            }
            
            // Fallback for layouts not handled as VerticalLayout (should not happen with SimpleFacadePlanner)
            // Create a simple quad for the entire height, assign to upper structure for now.
            AddFacadeQuad(segmentSpecificDraft, baseBottomLeft, baseBottomRight, height, segmentNormal3D, Color.magenta); // Magenta to indicate fallback
            return segmentSpecificDraft;
        }
        
        /// <summary>
        /// Helper to add a basic facade quad (background wall) to a MeshDraft.
        /// </summary>
        private void AddFacadeQuad(MeshDraft draft, Vector3 bottomLeft, Vector3 bottomRight, float height, Vector3 normal, Color color)
        {
            Vector3 topLeft = bottomLeft + Vector3.up * height;
            Vector3 topRight = bottomRight + Vector3.up * height;

            int vertexCount = draft.vertices.Count;
            draft.vertices.Add(bottomLeft);
            draft.vertices.Add(bottomRight);
            draft.vertices.Add(topRight);
            draft.vertices.Add(topLeft);
            
            for (int i = 0; i < 4; i++)
            {
                draft.normals.Add(normal);
                draft.colors.Add(color); // Assign color to vertices
            }
            
            draft.uv.Add(new Vector2(0, 0));
            draft.uv.Add(new Vector2(1, 0));
            draft.uv.Add(new Vector2(1, 1));
            draft.uv.Add(new Vector2(0, 1));
            
            draft.triangles.Add(vertexCount);
            draft.triangles.Add(vertexCount + 1);
            draft.triangles.Add(vertexCount + 2);
            
            draft.triangles.Add(vertexCount);
            draft.triangles.Add(vertexCount + 2);
            draft.triangles.Add(vertexCount + 3);
        }
        
        /// <summary>
        /// 确保网格完整性，为每个顶点提供UV坐标和颜色
        /// </summary>
        private void EnsureMeshIntegrity(MeshDraft meshDraft)
        {
            int vertexCount = meshDraft.vertices.Count;
            
            // 确保UV数组与顶点数组长度一致
            while (meshDraft.uv.Count < vertexCount)
            {
                meshDraft.uv.Add(new Vector2(0, 0));
            }
            
            // 确保颜色数组与顶点数组长度一致
            while (meshDraft.colors.Count < vertexCount)
            {
                meshDraft.colors.Add(Color.white);
            }
            
            // 确保法线数组与顶点数组长度一致
            while (meshDraft.normals.Count < vertexCount)
            {
                meshDraft.normals.Add(Vector3.up);
            }
        }
        
        /// <summary>
        /// 添加商铺
        /// </summary>
        private void AddShop(MeshDraft wallDraft, Vector3 bottomLeft, Vector3 direction, Vector3 normal, ShopLayout shopLayout)
        {
            Debug.Log($"[PTSimplifiedBuildingGenerator] AddShop CALLED. bottomLeft.y: {bottomLeft.y}, shopLayout.height: {shopLayout.height}, shopLayout.origin.y: {shopLayout.origin.y}");

            float shopWidth = shopLayout.width;
            float shopHeight = shopLayout.height;
            Vector3 normalVec = new Vector3(normal.x, 0, normal.y);
            
            // 计算可容纳的窗户数量（去除门占用的空间后）
            float totalDoorWidth = 0;
            foreach (var doorPos in shopLayout.doorPositions)
            {
                totalDoorWidth += shopLayout.doorWidth;
            }
            
            float remainingWidth = shopWidth - totalDoorWidth;
            int windowCount = Mathf.FloorToInt(remainingWidth / (shopLayout.shopWindowWidth + 0.5f));
            
            // 如果没有足够空间放窗户，直接返回
            if (windowCount <= 0 && shopLayout.doorPositions.Count == 0)
            {
                return;
            }
            
            // 为门和窗户计算位置
            List<float> windowPositions = new List<float>();
            List<bool> isWindow = new List<bool>(); // true=窗户，false=门
            
            // 首先放置所有门
            foreach (var doorPos in shopLayout.doorPositions)
            {
                windowPositions.Add(doorPos.x);
                isWindow.Add(false);
            }
            
            // 如果有窗户要放置
            if (windowCount > 0)
            {
                // 计算窗户间隔
                float windowSpacing = remainingWidth / windowCount;
                
                // 放置窗户
                for (int i = 0; i < windowCount; i++)
                {
                    // 按照剩余空间均匀分布窗户
                    float xPos = i * windowSpacing + shopLayout.shopWindowWidth / 2;
                    bool positionTaken = false;
                    
                    // 检查这个位置是否已经有门
                    foreach (var doorPos in shopLayout.doorPositions)
                    {
                        if (Mathf.Abs(xPos - (doorPos.x + shopLayout.doorWidth / 2)) < shopLayout.shopWindowWidth)
                        {
                            positionTaken = true;
                            break;
                        }
                    }
                    
                    if (!positionTaken)
                    {
                        windowPositions.Add(xPos - shopLayout.shopWindowWidth / 2);
                        isWindow.Add(true);
                    }
                }
            }
            
            // 按位置排序
            for (int i = 0; i < windowPositions.Count; i++)
            {
                for (int j = i + 1; j < windowPositions.Count; j++)
                {
                    if (windowPositions[i] > windowPositions[j])
                    {
                        // 交换位置
                        float tempPos = windowPositions[i];
                        windowPositions[i] = windowPositions[j];
                        windowPositions[j] = tempPos;
                        
                        // 交换类型
                        bool tempType = isWindow[i];
                        isWindow[i] = isWindow[j];
                        isWindow[j] = tempType;
                    }
                }
            }
            
            // 添加所有窗户和门
            for (int i = 0; i < windowPositions.Count; i++)
            {
                float xPos = windowPositions[i];
                
                if (isWindow[i])
                {
                    // 添加窗户
                    Vector3 windowCenter = bottomLeft + direction * xPos + Vector3.up * (shopLayout.shopWindowHeight / 2 + 0.5f);
                    windowCenter += direction * (shopLayout.shopWindowWidth / 2);
                    
                    AddWindow(wallDraft, windowCenter, shopLayout.shopWindowWidth, shopLayout.shopWindowHeight, direction, normalVec);
                }
                else
                {
                    // 添加门
                    Vector3 doorCenter = bottomLeft + direction * xPos + Vector3.up * (shopLayout.doorHeight / 2);
                    doorCenter += direction * (shopLayout.doorWidth / 2);
                    
                    AddDoor(wallDraft, doorCenter, shopLayout.doorWidth, shopLayout.doorHeight, direction, normalVec);
                }
            }
            
            // 添加商铺顶棚（平顶样式）
            // AddShopCeiling(wallDraft, bottomLeft, direction, normal, shopWidth, shopHeight); // Old call, wallDraft is for the facade segment.
            // The ceiling should be part of the shopSegmentGO created in CreateWallDraft or its own GO under shopsParent.
            // For now, let's assume AddShopCeiling will be called where it can access the correct parent transform.
            // This means AddShopCeiling might need to be called from CreateWallDraft, after shopSegmentGO is made.
        }
        
        /// <summary>
        /// 添加商铺顶棚
        /// </summary>
        private void AddShopCeiling(Transform shopSegmentParent, float segmentWidth, float shopHeight, Color ceilingColor)
        {
            GameObject ceilingGO = new GameObject("ShopCeiling");
            ceilingGO.transform.SetParent(shopSegmentParent, false); 
            // shopSegmentParent is already oriented. Ceiling is on top of the shop.
            ceilingGO.transform.localPosition = new Vector3(0, shopHeight, 0); 
            ceilingGO.transform.localRotation = Quaternion.identity; // Align with parent's orientation

            MeshFilter mf = ceilingGO.AddComponent<MeshFilter>();
            MeshRenderer mr = ceilingGO.AddComponent<MeshRenderer>();
            var ceilingDraft = new MeshDraft { name = "ShopCeilingMesh" };
            
            // Ceiling vertices are now in the XY plane of ceilingGO, which is already at shopHeight
            float overhang = 0.0f; // No overhang for simplicity, or make it a parameter
            Vector3 cbl = new Vector3(-overhang, 0, -overhang); // Bottom-left relative to center of ceiling segment if desired, or from corner
            Vector3 cbr = new Vector3(segmentWidth + overhang, 0, -overhang);
            Vector3 ctl = new Vector3(-overhang, 0, overhang); // This was an error, ceiling is flat. Z should be width for a quad if using directions
            Vector3 ctr = new Vector3(segmentWidth + overhang, 0, overhang);
            
            // Let's define from bottom-left corner of the segmentWidth
            cbl = new Vector3(0, 0, 0);
            cbr = new Vector3(segmentWidth, 0, 0);
            ctl = new Vector3(0, 0, 0); // This definition is for a quad along local X axis for segmentWidth. What is the depth?
                                      // The original ceiling was aligned with the wall segment. 
                                      // If shopSegmentParent is oriented with Z along wall, X is depth.
            // Let's assume a small depth for the ceiling, e.g., 0.2f, extending 'inward/outward' from wall line
            float ceilingDepth = 0.2f; 
            // Vertices for a quad in XZ plane of ceilingGO (which is aligned with shopSegmentParent)
            // shopSegmentParent's Z is along the wall. X is perpendicular (depth).
            cbl = new Vector3(0, 0, 0);              // Front-left corner of ceiling, along the wall line
            cbr = new Vector3(segmentWidth, 0, 0);   // Front-right corner
            ctl = new Vector3(0, 0, ceilingDepth);     // Back-left corner (assuming positive Z is 'inward' or choose a direction)
            ctr = new Vector3(segmentWidth, 0, ceilingDepth);  // Back-right corner

            ceilingDraft.vertices.Add(cbl); 
            ceilingDraft.vertices.Add(cbr); 
            ceilingDraft.vertices.Add(ctr); 
            ceilingDraft.vertices.Add(ctl);

            Vector3 ceilingNormal = Vector3.up;
            for (int i = 0; i < 4; i++)
            {
                ceilingDraft.normals.Add(ceilingNormal);
                ceilingDraft.colors.Add(ceilingColor); 
            }
            
            ceilingDraft.uv.Add(new Vector2(0, 0));
            ceilingDraft.uv.Add(new Vector2(1, 0));
            ceilingDraft.uv.Add(new Vector2(1, 1));
            ceilingDraft.uv.Add(new Vector2(0, 1));
            
            ceilingDraft.triangles.Add(0); // cbl
            ceilingDraft.triangles.Add(1); // cbr
            ceilingDraft.triangles.Add(2); // ctr
            
            ceilingDraft.triangles.Add(0); // cbl
            ceilingDraft.triangles.Add(2); // ctr
            ceilingDraft.triangles.Add(3); // ctl
            
            EnsureMeshIntegrity(ceilingDraft); 
            mf.mesh = ceilingDraft.ToMesh();

            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = ceilingColor;
            mr.material = mat;
        }
        
        /// <summary>
        /// 添加门
        /// </summary>
        private void AddDoor(MeshDraft wallDraft, Vector3 center, float width, float height, Vector3 wallDirection, Vector3 normal)
        {
            // 门的顶点
            Vector3 bottomLeft = center - wallDirection * (width / 2) - Vector3.up * (height / 2);
            Vector3 bottomRight = center + wallDirection * (width / 2) - Vector3.up * (height / 2);
            Vector3 topLeft = center - wallDirection * (width / 2) + Vector3.up * (height / 2);
            Vector3 topRight = center + wallDirection * (width / 2) + Vector3.up * (height / 2);
            
            // 门的深度偏移
            float doorDepth = 0.05f;
            bottomLeft += normal * doorDepth;
            bottomRight += normal * doorDepth;
            topLeft += normal * doorDepth;
            topRight += normal * doorDepth;
            
            // 创建门
            var doorDraft = new MeshDraft { name = "Door" };
            
            // 添加门顶点
            doorDraft.vertices.Add(bottomLeft);
            doorDraft.vertices.Add(bottomRight);
            doorDraft.vertices.Add(topRight);
            doorDraft.vertices.Add(topLeft);
            
            // 添加门法线
            Vector3 doorNormal = -normal;
            for (int i = 0; i < 4; i++)
            {
                doorDraft.normals.Add(doorNormal);
            }
            
            // 添加UV坐标
            doorDraft.uv.Add(new Vector2(0, 0));
            doorDraft.uv.Add(new Vector2(1, 0));
            doorDraft.uv.Add(new Vector2(1, 1));
            doorDraft.uv.Add(new Vector2(0, 1));
            
            // 添加颜色，用于标识门
            for (int i = 0; i < 4; i++)
            {
                doorDraft.colors.Add(new Color(1.0f, 0f, 0f, 1.0f)); // R=1 for Door
            }
            
            // 添加两个三角形的索引
            doorDraft.triangles.Add(0);
            doorDraft.triangles.Add(1);
            doorDraft.triangles.Add(2);
            
            doorDraft.triangles.Add(0);
            doorDraft.triangles.Add(2);
            doorDraft.triangles.Add(3);
            
            // 合并到主墙面
            wallDraft.Add(doorDraft);
            
            // 添加门框
            float frameWidth = 0.1f;
            
            // 左框
            AddWindowFrame(
                wallDraft,
                bottomLeft - wallDirection * frameWidth,
                bottomLeft,
                topLeft,
                topLeft - wallDirection * frameWidth,
                normal,
                new Color(0.5f, 0.5f, 0.5f, 1.0f) // Grey for Frame
            );
            
            // 右框
            AddWindowFrame(
                wallDraft,
                bottomRight,
                bottomRight + wallDirection * frameWidth,
                topRight + wallDirection * frameWidth,
                topRight,
                normal,
                new Color(0.5f, 0.5f, 0.5f, 1.0f) // Grey for Frame
            );
            
            // 上框
            AddWindowFrame(
                wallDraft,
                topLeft,
                topRight,
                topRight + Vector3.up * frameWidth,
                topLeft + Vector3.up * frameWidth,
                normal,
                new Color(0.5f, 0.5f, 0.5f, 1.0f) // Grey for Frame
            );
        }
        
        /// <summary>
        /// 添加标准窗户
        /// </summary>
        private void AddStandardWindows(MeshDraft wallDraft, Vector3 bottomLeft, Vector3 bottomRight, float wallHeight, float yOffset, Vector3 normal, int configuredFloorCount = 0)
        {
            if (wallHeight <= yOffset)
            {
                return; // 没有空间放窗户
            }
            
                float windowWidth = 1.2f;
                float windowHeight = 1.8f;
            float wallLength = (bottomRight - bottomLeft).magnitude;
                
                // 计算可容纳的窗户数量
                int windowCount = Mathf.FloorToInt(wallLength / 3.0f);
            
            // 获取全局楼层高度（与外部一致）
            float globalFloorHeight = this.facadePlanner != null ? this.facadePlanner.floorHeight * this.facadePlanner.heightMultiplier : 3.5f;
                
                if (windowCount > 0)
                {
                    float spacing = wallLength / windowCount;
                    
                    // 墙面向量和法线
                    Vector3 wallVec = (bottomRight - bottomLeft).normalized;
                Vector3 normalVec = new Vector3(normal.x, 0, normal.z);
                
                // 使用配置的楼层数或者根据高度计算楼层数
                int floorCount = configuredFloorCount;
                if (floorCount <= 0)
                {
                    // 如果没有配置楼层数，则根据高度计算
                    floorCount = Mathf.Max(1, Mathf.RoundToInt((wallHeight - yOffset) / globalFloorHeight));
                }
                // 重新计算楼层间距，使楼层均匀分布
                float floorSpacing = globalFloorHeight;
                // 强制每层高度一致，数量等于floorCount
                    for (int i = 0; i < windowCount; i++)
                    {
                        for (int floor = 0; floor < floorCount; floor++)
                        {
                            float xPos = (i + 0.5f) * spacing;
                        float yPos = yOffset + floor * floorSpacing + (floorSpacing * 0.3f);
                            Vector3 windowCenter = bottomLeft + wallVec * xPos + Vector3.up * yPos;
                            AddWindow(wallDraft, windowCenter, windowWidth, windowHeight, wallVec, normalVec);
                        }
                    }
                }
        }
        
        /// <summary>
        /// 添加窗户
        /// </summary>
        private void AddWindow(MeshDraft wallDraft, Vector3 center, float width, float height, Vector3 wallDirection, Vector3 normal)
        {
            // 窗户顶点
            Vector3 bottomLeft = center - wallDirection * (width / 2) - Vector3.up * (height / 2);
            Vector3 bottomRight = center + wallDirection * (width / 2) - Vector3.up * (height / 2);
            Vector3 topLeft = center - wallDirection * (width / 2) + Vector3.up * (height / 2);
            Vector3 topRight = center + wallDirection * (width / 2) + Vector3.up * (height / 2);
            
            // 窗户深度偏移
            float windowDepth = 0.05f;
            bottomLeft += normal * windowDepth;
            bottomRight += normal * windowDepth;
            topLeft += normal * windowDepth;
            topRight += normal * windowDepth;
            
            // 创建窗户玻璃部分 - 使用单独的MeshDraft以便后续设置不同材质
            var glassDraft = new MeshDraft { name = "WindowGlass" };
            
            // 添加窗户玻璃顶点
            glassDraft.vertices.Add(bottomLeft);
            glassDraft.vertices.Add(bottomRight);
            glassDraft.vertices.Add(topRight);
            glassDraft.vertices.Add(topLeft);
            
            // 添加窗户法线 (朝向外部)
            Vector3 glassNormal = -normal; // 窗户法线朝外
            for (int i = 0; i < 4; i++)
            {
                glassDraft.normals.Add(glassNormal);
            }
            
            // 添加UV坐标
            glassDraft.uv.Add(new Vector2(0, 0));
            glassDraft.uv.Add(new Vector2(1, 0));
            glassDraft.uv.Add(new Vector2(1, 1));
            glassDraft.uv.Add(new Vector2(0, 1));
            
            // 添加颜色，用于标识玻璃材质 - 确保蓝色分量大于0.9
            for (int i = 0; i < 4; i++)
            {
                glassDraft.colors.Add(new Color(0, 0, 1.0f, 0.5f)); // B=1 for Glass
            }
            
            // 添加两个三角形的索引
            glassDraft.triangles.Add(0);
            glassDraft.triangles.Add(1);
            glassDraft.triangles.Add(2);
            
            glassDraft.triangles.Add(0);
            glassDraft.triangles.Add(2);
            glassDraft.triangles.Add(3);
            
            // 合并到主墙面
            wallDraft.Add(glassDraft);
            
            // 添加窗框
            float frameWidth = 0.1f;
            
            // 左框
            AddWindowFrame(
                wallDraft,
                bottomLeft - wallDirection * frameWidth,
                bottomLeft,
                topLeft,
                topLeft - wallDirection * frameWidth,
                normal,
                new Color(0.5f, 0.5f, 0.5f, 1.0f) // Grey for Frame
            );
            
            // 右框
            AddWindowFrame(
                wallDraft,
                bottomRight,
                bottomRight + wallDirection * frameWidth,
                topRight + wallDirection * frameWidth,
                topRight,
                normal,
                new Color(0.5f, 0.5f, 0.5f, 1.0f) // Grey for Frame
            );
            
            // 上框
            AddWindowFrame(
                wallDraft,
                topLeft,
                topRight,
                topRight + Vector3.up * frameWidth,
                topLeft + Vector3.up * frameWidth,
                normal,
                new Color(0.5f, 0.5f, 0.5f, 1.0f) // Grey for Frame
            );
            
            // 下框
            AddWindowFrame(
                wallDraft,
                bottomLeft - Vector3.up * frameWidth,
                bottomRight - Vector3.up * frameWidth,
                bottomRight,
                bottomLeft,
                normal,
                new Color(0.5f, 0.5f, 0.5f, 1.0f) // Grey for Frame
            );
        }
        
        /// <summary>
        /// 添加窗框四边形
        /// </summary>
        private void AddWindowFrame(MeshDraft draft, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4, Vector3 normal, Color frameColor)
        {
            // 获取当前顶点数
            int vertexCount = draft.vertices.Count;
            
            // 添加窗框顶点
            draft.vertices.Add(v1);
            draft.vertices.Add(v2);
            draft.vertices.Add(v3);
            draft.vertices.Add(v4);
            
            // 添加法线
            Vector3 frameNormal = -normal;
            for (int i = 0; i < 4; i++)
            {
                draft.normals.Add(frameNormal);
                draft.colors.Add(frameColor); // Use passed frame color
            }
            
            // 添加UV坐标
            draft.uv.Add(new Vector2(0, 0));
            draft.uv.Add(new Vector2(1, 0));
            draft.uv.Add(new Vector2(1, 1));
            draft.uv.Add(new Vector2(0, 1));
            
            // 添加两个三角形的索引
            draft.triangles.Add(vertexCount);
            draft.triangles.Add(vertexCount + 1);
            draft.triangles.Add(vertexCount + 2);
            
            draft.triangles.Add(vertexCount);
            draft.triangles.Add(vertexCount + 2);
            draft.triangles.Add(vertexCount + 3);
        }
        
        /// <summary>
        /// 创建建筑底座网格草稿
        /// </summary>
        private MeshDraft CreateFoundationDraft(List<Vector2> foundationPolygon)
        {
            // 创建底座网格
            var foundation = new MeshDraft { name = "Foundation" };
            
            // 创建底座顶点列表（3D坐标）
            var foundationVertices = new List<Vector3>();
            foreach (var point in foundationPolygon)
            {
                foundationVertices.Add(new Vector3(point.x, 0, point.y));
            }
            
            // 获取当前顶点数
            int startVertexCount = foundation.vertices.Count;
            
            // 设置底座为多边形
            foundation.AddTriangleFan(foundationVertices);
            
            // 确保新添加的所有顶点都有UV和颜色
            int newVertexCount = foundation.vertices.Count;
            for (int i = startVertexCount; i < newVertexCount; i++)
            {
                // 如果AddTriangleFan没有添加UV，我们手动添加
                if (foundation.uv.Count <= i)
                {
                    // 生成从中心到边缘的UV坐标
                    Vector3 localPos = foundation.vertices[i];
                    float u = (localPos.x + 50) / 100f; // 简单映射，假设建筑在[-50,50]范围内
                    float v = (localPos.z + 50) / 100f;
                    foundation.uv.Add(new Vector2(u, v));
                }
                
                // 如果AddTriangleFan没有添加颜色，我们手动添加
                if (foundation.colors.Count <= i)
                {
                    foundation.colors.Add(Color.white);
                }
            }
            
            return foundation;
        }
    }
    
    /// <summary>
    /// 简单的屋顶规划器
    /// </summary>
    public class SimpleRoofPlanner : IRoofPlanner
    {
        public IConstructible<MeshDraft> Plan(List<Vector2> foundationPolygon, BuildingGenerator.Config config)
        {
            // 根据配置创建不同类型的屋顶
            switch (config.roofConfig.type)
            {
                case RoofType.Hipped:
                    return new ProceduralHippedRoof(foundationPolygon, config.roofConfig, config.palette.roofColor);
                case RoofType.Gabled:
                    return new ProceduralGabledRoof(foundationPolygon, config.roofConfig, config.palette.roofColor);
                case RoofType.Flat:
                default:
                    return new ProceduralFlatRoof(foundationPolygon, config.roofConfig, config.palette.roofColor);
            }
        }
    }
    
    /// <summary>
    /// 简单的屋顶构造器
    /// </summary>
    public class SimpleRoofConstructor : IRoofConstructor
    {
        public PTSimplifiedBuildingGenerator generatorContext;

        public void Construct(IConstructible<MeshDraft> constructible, Transform parent)
        {
            var roofDraft = constructible.Construct(Vector2.zero);
            EnsureMeshIntegrity(roofDraft);
            var roofObject = new GameObject("UpperBuildingRoof");
            
            Transform upperStructureParent = parent.Find("UpperStructure");
            roofObject.transform.SetParent(upperStructureParent != null ? upperStructureParent : parent, false);
            
            // 直接设置屋顶位置的y轴为0
            roofObject.transform.localPosition = Vector3.zero;
            
            var meshFilter = roofObject.AddComponent<MeshFilter>();
            var meshRenderer = roofObject.AddComponent<MeshRenderer>();
            meshFilter.sharedMesh = roofDraft.ToMesh();
            
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (generatorContext != null)
            {
                material.color = generatorContext.upperRoofColor;
            }
            else
            {
                material.color = new Color(0.5f, 0.2f, 0.2f); 
            }
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.SetFloat("_Smoothness", 0.1f);
            meshRenderer.sharedMaterial = material;
        }
        
        /// <summary>
        /// 确保网格完整性，为每个顶点提供UV坐标和颜色
        /// </summary>
        private void EnsureMeshIntegrity(MeshDraft meshDraft)
        {
            int vertexCount = meshDraft.vertices.Count;
            
            // 确保UV数组与顶点数组长度一致
            while (meshDraft.uv.Count < vertexCount)
            {
                meshDraft.uv.Add(new Vector2(0, 0));
            }
            
            // 确保颜色数组与顶点数组长度一致
            while (meshDraft.colors.Count < vertexCount)
            {
                meshDraft.colors.Add(Color.white);
            }
            
            // 确保法线数组与顶点数组长度一致
            while (meshDraft.normals.Count < vertexCount)
            {
                meshDraft.normals.Add(Vector3.up);
            }
        }
    }

    public static class MeshDraftExtensions
    {
        // Helper to create a mesh with submeshes based on vertex colors
        public static Mesh ToMeshWithSubMeshes(this MeshDraft draft, out Material[] materialsOut, PTSimplifiedBuildingGenerator generatorContext)
        {
            if (draft.vertices.Count == 0)
            {
                materialsOut = null; 
                return new Mesh();
            }

            Mesh mesh = draft.ToMesh(); 
            if (mesh.vertexCount == 0)
            {
                materialsOut = null; 
                return mesh;
            }

            Color[] colors = mesh.colors;
            if (colors == null || colors.Length == 0)
            {
                materialsOut = new Material[1];
                materialsOut[0] = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                materialsOut[0].color = Color.white; 
                return mesh;
            }

            List<int> wallTriangles = new List<int>();
            List<int> glassTriangles = new List<int>();
            List<int> doorTriangles = new List<int>();
            List<int> frameTriangles = new List<int>();

            int[] triangles = mesh.triangles;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int i1 = triangles[i];
                Color vertexColor = (i1 < colors.Length) ? colors[i1] : Color.white;

                if (Mathf.Approximately(vertexColor.b, 1.0f) && vertexColor.r < 0.1f && vertexColor.g < 0.1f) 
                {
                    glassTriangles.Add(triangles[i]);
                    glassTriangles.Add(triangles[i + 1]);
                    glassTriangles.Add(triangles[i + 2]);
                }
                else if (Mathf.Approximately(vertexColor.r, 1.0f) && vertexColor.g < 0.1f && vertexColor.b < 0.1f) 
                {
                    doorTriangles.Add(triangles[i]);
                    doorTriangles.Add(triangles[i + 1]);
                    doorTriangles.Add(triangles[i + 2]);
                }
                else if (Mathf.Approximately(vertexColor.r, 0.5f) && Mathf.Approximately(vertexColor.g, 0.5f) && Mathf.Approximately(vertexColor.b, 0.5f)) 
                {
                    frameTriangles.Add(triangles[i]);
                    frameTriangles.Add(triangles[i + 1]);
                    frameTriangles.Add(triangles[i + 2]);
                }
                else 
                {
                    wallTriangles.Add(triangles[i]);
                    wallTriangles.Add(triangles[i + 1]);
                    wallTriangles.Add(triangles[i + 2]);
                }
            }

            List<Material> activeMaterials = new List<Material>();
            // Calculate the number of submeshes needed first
            int finalSubMeshCount = 0;
            if (wallTriangles.Count > 0) finalSubMeshCount++;
            if (glassTriangles.Count > 0) finalSubMeshCount++;
            if (doorTriangles.Count > 0) finalSubMeshCount++;
            if (frameTriangles.Count > 0) finalSubMeshCount++;

            mesh.subMeshCount = finalSubMeshCount; // Set it once
            int currentSubMeshIndex = 0;

            if (wallTriangles.Count > 0)
            {
                mesh.SetTriangles(wallTriangles, currentSubMeshIndex);
                Material wallMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                activeMaterials.Add(wallMat);
                currentSubMeshIndex++;
            }
            if (glassTriangles.Count > 0)
            {
                mesh.SetTriangles(glassTriangles, currentSubMeshIndex);
                Material glassMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                glassMat.SetFloat("_Surface", 1); 
                glassMat.SetFloat("_Blend", 0); 
                glassMat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                glassMat.renderQueue = 3000;
                glassMat.color = generatorContext.glassColor; 
                activeMaterials.Add(glassMat);
                currentSubMeshIndex++;
            }
            if (doorTriangles.Count > 0)
            {
                mesh.SetTriangles(doorTriangles, currentSubMeshIndex);
                Material doorMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                doorMat.color = generatorContext.doorColor;
                activeMaterials.Add(doorMat);
                currentSubMeshIndex++;
            }
            if (frameTriangles.Count > 0)
            {
                mesh.SetTriangles(frameTriangles, currentSubMeshIndex);
                Material frameMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                frameMat.color = generatorContext.windowFrameColor;
                activeMaterials.Add(frameMat);
                currentSubMeshIndex++;
            }
            materialsOut = activeMaterials.ToArray();
            return mesh;
        }
    }
} 