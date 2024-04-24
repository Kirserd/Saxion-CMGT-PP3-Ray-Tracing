using UnityEngine;
using MIRAI.RayTracing.Primitives;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;

namespace MIRAI.RayTracing.Primitives 
{
    struct Sphere
    {
        public Vector3 Position;
        public float Radius;
        public Vector3 Albedo;
        public Vector3 Specular;
        public float Roughness;
    }
}

namespace MIRAI.RayTracing
{
    public class RayTracingMaster : MonoBehaviour
    {
        [Header("CORE")]
        [SerializeField] private ComputeShader _rayTracingShader;
        [SerializeField] private Shader _progressiveSamplingShader;

        [Space(10)]

        [Header("PARAMETERS")]
        [SerializeField] private Texture _skyboxTexture;
        [Space(10)]
        [SerializeField] private bool    _progressiveSampling;
        [SerializeField] private uint    _progressiveSamplingMaxSamples;
        [Space(10)]
        [SerializeField] private Vector2 _sphereRadius = new Vector2(3.0f, 8.0f);
        [SerializeField] private uint    _spheresMax = 100;
        [SerializeField] private float   _spherePlacementRadius = 100.0f;

        private RenderTexture _target;
        private Camera _camera;
        private Light _directionalLight;

        private uint _currentSample = 0;
        private Material _progressiveSamplingMaterial;

        private ComputeBuffer _sphereBuffer;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _progressiveSamplingMaterial = new Material(_progressiveSamplingShader);

            _directionalLight = GameObject.FindGameObjectWithTag("MainLight").GetComponent<Light>();

            SetUpScene();
        }
        private void Update()
        {
            if (transform.hasChanged || _directionalLight.transform.hasChanged)
            {
                _currentSample = 0;

                transform.hasChanged = false;
                _directionalLight.transform.hasChanged = false;
            }
        }

        private void OnValidate() => _currentSample = 0;

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            SetShaderParameters();
            Render(destination);
        }
        private void SetShaderParameters()
        {
            Vector3 dirLightFwd = _directionalLight.transform.forward;

            _rayTracingShader.SetMatrix("_CameraToWorld", _camera.cameraToWorldMatrix);
            _rayTracingShader.SetMatrix("_CameraInverseProjection", _camera.projectionMatrix.inverse);
            _rayTracingShader.SetTexture(0, "_SkyboxTexture", _skyboxTexture);
            _rayTracingShader.SetVector("_PixelOffset", new Vector2(Random.value, Random.value));
            _rayTracingShader.SetVector("_DirectionalLight", new Vector4(dirLightFwd.x, dirLightFwd.y, dirLightFwd.z, _directionalLight.intensity));
            _rayTracingShader.SetBuffer(0, "_Spheres", _sphereBuffer);
        }
        private void Render(RenderTexture destination)
        {
            InitRenderTexture();

            _rayTracingShader.SetTexture(0, "Result", _target);
            int threadGroupsX = Mathf.CeilToInt(Screen.width / 8.0f);
            int threadGroupsY = Mathf.CeilToInt(Screen.height / 8.0f);
            _rayTracingShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

            if (_progressiveSampling)
            {
                _progressiveSamplingMaterial.SetFloat("_Sample", _currentSample);
                Graphics.Blit(_target, destination, _progressiveSamplingMaterial);

                if (_currentSample < _progressiveSamplingMaxSamples)
                    _currentSample++;
                else
                    _currentSample = uint.MaxValue;
            }
            else
            {
                Graphics.Blit(_target, destination, _progressiveSamplingMaterial);
            }
        }

        private void InitRenderTexture()
        {
            if (_target == null || _target.width != Screen.width || _target.height != Screen.height)
            {
                if (_target != null)
                    _target.Release();

                _target = new(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                _target.enableRandomWrite = true;
                _target.Create();

                _currentSample = 0;
            }
        }

        private void OnEnable()
        {
            _currentSample = 0;
            SetUpScene();
        }

        private void OnDisable()
        {
            if (_sphereBuffer != null)
                _sphereBuffer.Release();
        }

        private void SetUpScene()
        {
            List<Sphere> spheres = new();

            for (int i = 0; i < _spheresMax; i++)
            {
                Sphere sphere = new Sphere();

                sphere.Radius = _sphereRadius.x + Random.value * (_sphereRadius.y - _sphereRadius.x);
                Vector2 randomPos = Random.insideUnitCircle * _spherePlacementRadius;
                sphere.Position = new Vector3(randomPos.x, sphere.Radius, randomPos.y);

                foreach (Sphere other in spheres)
                {
                    float minDist = sphere.Radius + other.Radius;
                    if (Vector3.SqrMagnitude(sphere.Position - other.Position) < minDist * minDist)
                        goto SkipSphere;
                }

                Color color = Random.ColorHSV();
                bool metal  = Random.value < 0.5f;
                sphere.Albedo   = metal ? Vector3.zero : new Vector3(color.r, color.g, color.b);
                sphere.Specular = metal ? new Vector3(color.r, color.g, color.b) : Vector3.one * 0.04f;
                sphere.Roughness = Random.value; 

                spheres.Add(sphere);

            SkipSphere:
                continue;
            }

            _sphereBuffer = new ComputeBuffer(spheres.Count, 44);
            _sphereBuffer.SetData(spheres);
        }
    }
}