using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class ComputeShaderTextureDispatcher : ScriptableRendererFeature
{
    [SerializeField] private RenderPassEvent _renderEvent;
    [SerializeField] private ComputeShader _computeShader;
    [SerializeField] private string _textureName;
    [SerializeField] private string _kernelName;
    
    private Action<ComputeShader, RTHandle> OnBeforeDispatch;
    private Action<ComputeShader, RTHandle> OnAfterDispatch;

    class ComputeShaderTextureDispatcherPass : ScriptableRenderPass
    {
        internal ComputeShader _computeShader;
        
        internal Action<ComputeShader, RTHandle> OnBeforeDispatch;
        internal Action<ComputeShader, RTHandle> OnAfterDispatch;

        
        private string _textureName;
        private string _kernelName;

        private RTHandle _handle;
        private readonly int _handleNameID;

        private readonly ProfilingSampler _profilingSampler;
        private float _renderTextureWidth;
        private float _renderTextureHeight;

        public ComputeShaderTextureDispatcherPass( ComputeShader computeShader, string textureName, string kernelName )
        {
            _kernelName = kernelName; 
            _computeShader = computeShader;
            _textureName = textureName;

            _handle = RTHandles.Alloc( textureName, textureName );
            _handleNameID = Shader.PropertyToID( textureName );

            _profilingSampler = new ProfilingSampler( $"{textureName} Prepass" );
        }
        
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.enableRandomWrite = true;
            descriptor.msaaSamples = 1;
            RenderingUtils.ReAllocateIfNeeded( ref _handle, descriptor, name: _textureName );

            ConfigureTarget( _handle, renderingData.cameraData.renderer.cameraDepthTargetHandle );
            ConfigureClear( ClearFlag.Color, clearColor );

            _handle.rt.enableRandomWrite = true;
            _renderTextureWidth = descriptor.width;
            _renderTextureHeight = descriptor.height;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if ( !Application.isPlaying ) return;
            if ( ( renderingData.cameraData.cameraType & CameraType.Game ) == 0 ) return;

            var cmd = CommandBufferPool.Get( );

            using ( new ProfilingScope( cmd, _profilingSampler ) ) 
            {
                //executing all commands so the commands get put inside the profiling scope in the frame debugger
                context.ExecuteCommandBuffer( cmd );
                cmd.Clear( );

                var kernelIndex = _computeShader.FindKernel( _kernelName ); 
                _computeShader.GetKernelThreadGroupSizes( kernelIndex, out uint xGroupSize, out uint yGroupSize, out _ );
                cmd.SetComputeTextureParam( _computeShader, kernelIndex, _handleNameID, _handle );

                OnBeforeDispatch?.Invoke( _computeShader, _handle );
                
                cmd.DispatchCompute( _computeShader, kernelIndex,
                                     Mathf.CeilToInt( _renderTextureWidth / xGroupSize ),
                                     Mathf.CeilToInt( _renderTextureHeight / yGroupSize ),
                                     1 );

                OnAfterDispatch?.Invoke( _computeShader, _handle );
                cmd.SetGlobalTexture( _textureName, _handle );
            }

            //executing all commands so the commands get put inside the profiling scope in the frame debugger
            context.ExecuteCommandBuffer( cmd );
            cmd.Clear( );
            CommandBufferPool.Release( cmd );
        }

        public void ReleaseTarget( )
        {
            _handle?.Release( );
            OnBeforeDispatch = null;
            OnAfterDispatch = null;
        }
    }

    ComputeShaderTextureDispatcherPass m_ScriptablePass;

    public void SetBeforeDispatch( Action<ComputeShader, RTHandle> action) => OnBeforeDispatch+=action;
    public void SetAfterDispatch( Action<ComputeShader, RTHandle> action) => OnAfterDispatch+=action;

    /// <inheritdoc/>
    public override void Create()
    {
        m_ScriptablePass = new ComputeShaderTextureDispatcherPass(_computeShader, _textureName, _kernelName);
        
        InitializePass(  );
    }

    void InitializePass( )
    {
        m_ScriptablePass.OnBeforeDispatch = OnBeforeDispatch;
        m_ScriptablePass.OnAfterDispatch = OnAfterDispatch;

        // Configures where the render pass should be injected.
        m_ScriptablePass.renderPassEvent = _renderEvent;
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if(m_ScriptablePass._computeShader) renderer.EnqueuePass(m_ScriptablePass);
    }

    protected override void Dispose( bool disposing )
    {
        m_ScriptablePass.ReleaseTarget( );
        OnBeforeDispatch = null;
        OnAfterDispatch = null;
    }
}


