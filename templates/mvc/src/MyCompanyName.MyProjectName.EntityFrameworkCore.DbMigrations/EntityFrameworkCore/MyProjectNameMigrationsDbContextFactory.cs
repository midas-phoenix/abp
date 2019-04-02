﻿using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace MyCompanyName.MyProjectName.EntityFrameworkCore
{
    public class MyProjectNameMigrationsDbContextFactory : IDesignTimeDbContextFactory<MyProjectNameMigrationsDbContext>
    {
        public MyProjectNameMigrationsDbContext CreateDbContext(string[] args)
        {
            var configuration = BuildConfiguration();

            var builder = new DbContextOptionsBuilder<MyProjectNameMigrationsDbContext>()
                .UseSqlServer(configuration.GetConnectionString("Default"));

            return new MyProjectNameMigrationsDbContext(builder.Options);
        }

        private static IConfigurationRoot BuildConfiguration()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false);

            return builder.Build();
        }
    }
}