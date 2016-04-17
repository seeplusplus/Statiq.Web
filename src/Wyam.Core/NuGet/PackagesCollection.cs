﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NuGet;
using NuGet.Frameworks;
using Wyam.Common.IO;
using Wyam.Common.NuGet;
using Wyam.Common.Tracing;
using IFileSystem = Wyam.Common.IO.IFileSystem;

namespace Wyam.Core.NuGet
{
    internal class PackagesCollection : IPackagesCollection
    {
        private DirectoryPath _packagesPath = "packages";
        private DirectoryPath _contentPath = "content";
        private readonly IFileSystem _fileSystem;

        public PackagesCollection(IFileSystem fileSystem)
        {
            _fileSystem = fileSystem;
        }

        // The first repository is the default NuGet feed
        private readonly List<Repository> _repositories = new List<Repository>()
        {
            new Repository(null)
        };

        public DirectoryPath PackagesPath
        {
            get { return _packagesPath; }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(PackagesPath));
                }
                _packagesPath = value;
            }
        }

        private DirectoryPath AbsolutePackagesPath => _fileSystem.RootPath.Combine(PackagesPath).Collapse();

        public IRepository Repository(string packageSource)
        {
            Repository repository = new Repository(packageSource);
            _repositories.Add(repository);
            return repository;
        }

        public IRepository Install(string packageId, bool allowPrereleaseVersions, bool allowUnlisted = false)
        {
            return Install(packageId, null, allowPrereleaseVersions, allowUnlisted);
        }

        public IRepository Install(string packageId, string versionSpec = null,
            bool allowPrereleaseVersions = false, bool allowUnlisted = false)
        {
            _repositories[0].Install(packageId, versionSpec, allowPrereleaseVersions, allowUnlisted);
            return this;
        }

        public void InstallPackages(bool updatePackages)
        {
            foreach (Repository repository in _repositories)
            {
                repository.InstallPackages(AbsolutePackagesPath, _fileSystem, updatePackages);
            }
        }
        
        public IEnumerable<FilePath> GetCompatibleAssemblyPaths()
        {
            List<FilePath> assemblyPaths = new List<FilePath>();
            FrameworkReducer reducer = new FrameworkReducer();
            NuGetFramework targetFramework = new NuGetFramework(".NETFramework", Version.Parse("4.6"));  // TODO: If alternate versions of Wyam are developed (I.e., for DNX), this will need to be switched
            NuGetFrameworkFullComparer frameworkComparer = new NuGetFrameworkFullComparer();
            IPackageRepository packageRepository = PackageRepositoryFactory.Default.CreateRepository(AbsolutePackagesPath.FullPath);
            PackageManager packageManager = new PackageManager(packageRepository, AbsolutePackagesPath.FullPath);
            foreach (IPackage package in packageManager.LocalRepository.GetPackages())
            {
                List<KeyValuePair<IPackageFile, NuGetFramework>> filesAndFrameworks = package.GetLibFiles()
                    .Where(x => x.TargetFramework != null)
                    .Select(x => new KeyValuePair<IPackageFile, NuGetFramework>(x,
                        new NuGetFramework(x.TargetFramework.Identifier, x.TargetFramework.Version, x.TargetFramework.Profile)))
                    .ToList();
                NuGetFramework targetPackageFramework = reducer.GetNearest(targetFramework, filesAndFrameworks.Select(x => x.Value));
                if (targetPackageFramework != null)
                {
                    List<FilePath> packageAssemblyPaths = filesAndFrameworks
                        .Where(x => frameworkComparer.Equals(targetPackageFramework, x.Value))
                        .Select(x => AbsolutePackagesPath.Combine(String.Format(CultureInfo.InvariantCulture, "{0}.{1}", package.Id, package.Version)).CombineFile(x.Key.Path))
                        .Where(x => x.Extension == ".dll")
                        .ToList();
                    foreach (FilePath packageAssemblyPath in packageAssemblyPaths)
                    {
                        Trace.Verbose("Added assembly file {0} from package {1}.{2}", packageAssemblyPath.ToString(), package.Id, package.Version);
                    }
                    assemblyPaths.AddRange(packageAssemblyPaths);
                }
                else
                {
                    Trace.Verbose("Could not find compatible framework for package {0}.{1} (this is normal for content-only packages)", package.Id, package.Version);
                }
            }
            return assemblyPaths;
        }
    }
}
