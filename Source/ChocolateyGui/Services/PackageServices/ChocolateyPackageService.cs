﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright company="Chocolatey" file="ChocolateyPackageService.cs">
//   Copyright 2014 - Present Rob Reynolds, the maintainers of Chocolatey, and RealDimensions Software, LLC
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace ChocolateyGui.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using AutoMapper;
    using chocolatey;
    using chocolatey.infrastructure.app.domain;
    using chocolatey.infrastructure.results;
    using Enums;
    using NuGet;
    using Providers;
    using Utilities.Extensions;
    using ViewModels.Items;

    public class ChocolateyPackageService : BasePackageService, IChocolateyPackageService
    {
        private readonly IMapper _mapper;
        private readonly Func<IPackageViewModel> _packageFactory;

        public ChocolateyPackageService(
            IProgressService progressService,
            Func<string, ILogService> logFactory,
            IChocolateyConfigurationProvider chocolateyConfigurationProvider,
            IMapper mapper,
            Func<IPackageViewModel> packageFactory)
            : base(progressService, logFactory, chocolateyConfigurationProvider)
        {
            _mapper = mapper;
            _packageFactory = packageFactory;
        }

        public async Task<IEnumerable<IPackageViewModel>> GetInstalledPackages(bool force = false)
        {
            // Ensure that we only retrieve the packages one at a time to refresh the Cache.
            using (await GetInstalledLock.LockAsync())
            {
                ICollection<IPackageViewModel> packages;
                if (!force)
                {
                    packages = CachedPackages;

                    if (packages != null)
                    {
                        return packages;
                    }
                }

                StartProgressDialog("Chocolatey Service", "Retrieving installed packages...");

                var choco = Lets.GetChocolatey().Init(ProgressService, LogFactory);
                choco.Set(config =>
                {
                    config.CommandName = CommandNameType.list.ToString();
                    config.ListCommand.LocalOnly = true;
                    config.AllowUnofficialBuild = true;
#if !DEBUG
                config.Verbose = false;
#endif // DEBUG
                });

                var packageResults = await choco.ListAsync<PackageResult>();

                packages = packageResults
                    .Select(
                        package => _mapper.Map(package.Package, _packageFactory()))
                        .Select(package =>
                        {
                            package.IsInstalled = true;
                            return package;
                        }).ToList();

                CachedPackages = packages;

                await ProgressService.StopLoading();
                NotifyPackagesChanged(PackagesChangedEventType.Updated);
                return packages;
            }
        }

        public async Task InstallPackage(string id, SemanticVersion version = null, Uri source = null, bool force = false)
        {
            StartProgressDialog("Install Package", "Installing package", id);

            var choco = Lets.GetChocolatey().Init(ProgressService, LogFactory);
            choco.Set(config =>
            {
                config.CommandName = CommandNameType.install.ToString();
                config.PackageNames = id;
                config.AllowUnofficialBuild = true;
#if !DEBUG
                config.Verbose = false;
#endif // DEBUG

                if (version != null)
                {
                    config.Version = version.ToString();
                }

                if (source != null)
                {
                    config.Sources = source.ToString();
                }

                if (force)
                {
                    config.Force = true;
                }
            });

            await choco.RunAsync();

            await GetInstalledPackages(true);

            await InstalledPackage(id, version);
        }

        public async Task UninstallPackage(string id, SemanticVersion version, bool force = false)
        {
            StartProgressDialog("Uninstalling", "Uninstalling package", id);

            var choco = Lets.GetChocolatey().Init(ProgressService, LogFactory);
            choco.Set(config =>
            {
                config.CommandName = CommandNameType.uninstall.ToString();
                config.PackageNames = id;
                config.AllowUnofficialBuild = true;
#if !DEBUG
                config.Verbose = false;
#endif // DEBUG

                if (version != null)
                {
                    config.Version = version.ToString();
                }
            });

            await choco.RunAsync();

            await GetInstalledPackages(force: true);

            await UninstalledPackage(id, version);

            await ProgressService.StopLoading();
        }

        public async Task UpdatePackage(string id, Uri source = null)
        {
            StartProgressDialog("Updating", "Updating package", id);

            (await GetInstalledPackages()).FirstOrDefault(package => package.Id == id);

            var choco = Lets.GetChocolatey().Init(ProgressService, LogFactory);
            choco.Set(config =>
            {
                config.CommandName = CommandNameType.upgrade.ToString();
                config.PackageNames = id;
                config.AllowUnofficialBuild = true;
#if !DEBUG
                config.Verbose = false;
#endif // DEBUG
            });

            await choco.RunAsync();

            await GetInstalledPackages(true);

            await UpdatedPackage(id);
        }
    }
}