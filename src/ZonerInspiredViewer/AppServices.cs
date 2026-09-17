using System;
using System.IO;

namespace ZonerInspiredViewer
{
    internal sealed class AppServices : IDisposable
    {
        private InstanceWorkspace _workspace;
        public int InstanceNumber { get; private set; }
        public DecoderRegistry Decoders { get; private set; }
        public FolderCatalogService Catalog { get; private set; }
        public ThumbnailCacheService Thumbnails { get; private set; }
        public GpuThumbnailProcessor ThumbnailGpu { get; private set; }
        public VramImageCache VramCache { get; private set; }
        public SessionStore Sessions { get; private set; }
        public GpuMemoryPressureMonitor GpuPressure { get; private set; }
        public GpuImageCache GpuImages { get; private set; }
        public bool LowPriorityIccEnabled { get; set; }
        public int CpuWorkerCount { get; private set; }
        public string AppDataRoot { get; private set; }

        public static AppServices Create()
        {
            string profile = Environment.GetEnvironmentVariable("ZONER_VIEWER_PROFILE");
            return CreateInstance(String.IsNullOrWhiteSpace(profile) ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ZonerInspiredViewer") : profile);
        }

        internal static AppServices CreateInstance(string root)
        {
            InstanceWorkspace workspace = InstanceWorkspace.Acquire(root);
            try
            {
                AppServices services = Create(root);
                services.Sessions = new SessionStore(workspace.SessionPath, root, Path.Combine(root, "session.json"));
                services._workspace = workspace;
                services.InstanceNumber = workspace.Number;
                return services;
            }
            catch { workspace.Dispose(); throw; }
        }

        public void Dispose()
        {
            if (GpuImages != null) GpuImages.Dispose();
            if (ThumbnailGpu != null) ThumbnailGpu.Dispose();
            if (_workspace != null) _workspace.Dispose();
        }

        internal IDisposable AcquireMaintenance()
        {
            return InstanceWorkspace.AcquireMaintenance(AppDataRoot, _workspace == null ? 0 : _workspace.Number);
        }

        internal static AppServices Create(string root)
        {
            Directory.CreateDirectory(root);

            var services = new AppServices();
            services.InstanceNumber = 1;
            services.AppDataRoot = root;
            services.LowPriorityIccEnabled = false;
            services.CpuWorkerCount = Math.Max(4, Environment.ProcessorCount - 2);
            services.Decoders = new DecoderRegistry(delegate { return services.LowPriorityIccEnabled; });
            services.Catalog = new FolderCatalogService();
            services.GpuPressure = new GpuMemoryPressureMonitor();
            services.VramCache = new VramImageCache(1024, services.GpuPressure);
            services.GpuImages = new GpuImageCache(services.GpuPressure);
            services.Decoders.GpuImages = services.GpuImages;
            services.VramCache.Invalidated += services.GpuImages.Remove;
            services.VramCache.Cleared += services.GpuImages.Clear;
            services.ThumbnailGpu = new GpuThumbnailProcessor();
            services.Thumbnails = new ThumbnailCacheService(
                services.Decoders,
                Path.Combine(root, "thumbs"),
                Math.Max(4, Math.Min(24, services.CpuWorkerCount)), services.ThumbnailGpu);
            services.Sessions = new SessionStore(Path.Combine(root, "session.json"));
            return services;
        }

    }
}
