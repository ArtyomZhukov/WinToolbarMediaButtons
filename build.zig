const std = @import("std");

pub fn build(b: *std.Build) void {
    const target = b.standardTargetOptions(.{});
    const optimize = b.standardOptimizeOption(.{});

    const root_module = b.createModule(.{
        .root_source_file = b.path("src/main.zig"),
        .target = target,
        .optimize = optimize,
        .single_threaded = true,
    });

    const exe = b.addExecutable(.{
        .name = "WinToolbarMediaButtons",
        .root_module = root_module,
    });

    exe.subsystem = .Windows;
    exe.root_module.strip = true;

    b.installArtifact(exe);
}
