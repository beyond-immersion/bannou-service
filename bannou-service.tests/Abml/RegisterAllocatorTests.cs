// ═══════════════════════════════════════════════════════════════════════════
// ABML Register Allocator Tests
// Tests for register allocation behavior and edge cases.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Compiler;
using Xunit;

namespace BeyondImmersion.BannouService.Tests.Abml;

/// <summary>
/// Tests for the RegisterAllocator.
/// </summary>
public class RegisterAllocatorTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // BASIC ALLOCATION TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Allocate_ReturnsSequentialRegisters()
    {
        var allocator = new RegisterAllocator();

        Assert.Equal(0, allocator.Allocate());
        Assert.Equal(1, allocator.Allocate());
        Assert.Equal(2, allocator.Allocate());
    }

    [Fact]
    public void Allocate_TracksUsedCount()
    {
        var allocator = new RegisterAllocator();

        allocator.Allocate();
        allocator.Allocate();
        allocator.Allocate();

        Assert.Equal(3, allocator.UsedCount);
    }

    [Fact]
    public void Allocate_TracksAllocatedCount()
    {
        var allocator = new RegisterAllocator();

        allocator.Allocate();
        allocator.Allocate();
        allocator.Allocate();

        Assert.Equal(3, allocator.AllocatedCount);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FREE AND REUSE TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Free_AllowsRegisterReuse()
    {
        var allocator = new RegisterAllocator();

        var r0 = allocator.Allocate();
        var r1 = allocator.Allocate();

        allocator.Free(r0);

        // Next allocation should reuse freed register
        var r2 = allocator.Allocate();
        Assert.Equal(r0, r2);
    }

    [Fact]
    public void Free_DecreasesAllocatedCount()
    {
        var allocator = new RegisterAllocator();

        var r0 = allocator.Allocate();
        allocator.Allocate();

        Assert.Equal(2, allocator.AllocatedCount);

        allocator.Free(r0);

        Assert.Equal(1, allocator.AllocatedCount);
    }

    [Fact]
    public void Free_DoesNotDecreaseUsedCount()
    {
        var allocator = new RegisterAllocator();

        var r0 = allocator.Allocate();
        allocator.Allocate();

        Assert.Equal(2, allocator.UsedCount);

        allocator.Free(r0);

        // UsedCount tracks max ever used, not current
        Assert.Equal(2, allocator.UsedCount);
    }

    [Fact]
    public void Free_InvalidRegister_ThrowsException()
    {
        var allocator = new RegisterAllocator();

        allocator.Allocate(); // R0

        Assert.Throws<ArgumentOutOfRangeException>(() => allocator.Free(5));
    }

    [Fact]
    public void Free_StackBasedReuse()
    {
        var allocator = new RegisterAllocator();

        var r0 = allocator.Allocate();
        var r1 = allocator.Allocate();
        var r2 = allocator.Allocate();

        // Free in order: r1, r0
        allocator.Free(r1);
        allocator.Free(r0);

        // Reallocation should return in reverse order (stack)
        Assert.Equal(r0, allocator.Allocate()); // Last freed = first reused
        Assert.Equal(r1, allocator.Allocate());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // RANGE ALLOCATION TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AllocateRange_ReturnsContiguousRegisters()
    {
        var allocator = new RegisterAllocator();

        var start = allocator.AllocateRange(5);

        Assert.Equal(0, start);
        Assert.Equal(5, allocator.UsedCount);
    }

    [Fact]
    public void AllocateRange_InvalidCount_ThrowsException()
    {
        var allocator = new RegisterAllocator();

        Assert.Throws<ArgumentOutOfRangeException>(() => allocator.AllocateRange(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => allocator.AllocateRange(-1));
    }

    [Fact]
    public void AllocateRange_ExceedsLimit_ThrowsException()
    {
        var allocator = new RegisterAllocator();

        // Allocate close to max
        for (var i = 0; i < 250; i++)
        {
            allocator.Allocate();
        }

        // Try to allocate more than available
        Assert.Throws<InvalidOperationException>(() => allocator.AllocateRange(10));
    }

    [Fact]
    public void AllocateRange_FollowedByAllocate_ContinuesSequence()
    {
        var allocator = new RegisterAllocator();

        var rangeStart = allocator.AllocateRange(5); // R0-R4
        var next = allocator.Allocate(); // Should be R5

        Assert.Equal(0, rangeStart);
        Assert.Equal(5, next);
    }

    [Fact]
    public void FreeRange_AllowsRangeReuse()
    {
        var allocator = new RegisterAllocator();

        var start = allocator.AllocateRange(3);
        var afterRange = allocator.Allocate(); // R3

        allocator.FreeRange(start, 3);

        // Next allocations should reuse freed range (in stack order)
        Assert.Equal(2, allocator.Allocate()); // R2 (last pushed to free list)
        Assert.Equal(1, allocator.Allocate()); // R1
        Assert.Equal(0, allocator.Allocate()); // R0
    }

    // ═══════════════════════════════════════════════════════════════════════
    // RESET TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Reset_ClearsAllState()
    {
        var allocator = new RegisterAllocator();

        allocator.Allocate();
        allocator.Allocate();
        allocator.AllocateRange(3);

        allocator.Reset();

        Assert.Equal(0, allocator.UsedCount);
        Assert.Equal(0, allocator.AllocatedCount);
        Assert.Equal(0, allocator.Allocate()); // Should start from 0 again
    }

    [Fact]
    public void Reset_ClearsFreeList()
    {
        var allocator = new RegisterAllocator();

        var r0 = allocator.Allocate();
        allocator.Free(r0);

        allocator.Reset();

        // After reset, should allocate from 0, not from free list
        Assert.Equal(0, allocator.Allocate());
        Assert.Equal(1, allocator.Allocate());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // EDGE CASES
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Allocate_ManyRegisters_TracksCorrectly()
    {
        var allocator = new RegisterAllocator();

        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(i, allocator.Allocate());
        }

        Assert.Equal(100, allocator.UsedCount);
        Assert.Equal(100, allocator.AllocatedCount);
    }

    [Fact]
    public void AllocateAndFree_InterleavedOperations()
    {
        var allocator = new RegisterAllocator();

        var r0 = allocator.Allocate(); // 0
        var r1 = allocator.Allocate(); // 1
        allocator.Free(r0);            // Free 0
        var r2 = allocator.Allocate(); // Reuse 0
        var r3 = allocator.Allocate(); // 2
        allocator.Free(r1);            // Free 1
        allocator.Free(r2);            // Free 0
        var r4 = allocator.Allocate(); // Reuse 0 (last freed)
        var r5 = allocator.Allocate(); // Reuse 1

        Assert.Equal(0, r0);
        Assert.Equal(1, r1);
        Assert.Equal(0, r2); // Reused r0
        Assert.Equal(2, r3);
        Assert.Equal(0, r4); // Reused r2 which was r0
        Assert.Equal(1, r5); // Reused r1
    }

    [Fact]
    public void UsedCount_ReflectsHighWaterMark()
    {
        var allocator = new RegisterAllocator();

        allocator.Allocate(); // 0
        allocator.Allocate(); // 1
        allocator.Allocate(); // 2
        Assert.Equal(3, allocator.UsedCount);

        allocator.Free(0);
        allocator.Free(1);
        allocator.Free(2);
        Assert.Equal(3, allocator.UsedCount); // Still 3, not 0

        allocator.Allocate(); // Reuses 2
        allocator.Allocate(); // Reuses 1
        Assert.Equal(3, allocator.UsedCount); // Still 3

        allocator.Allocate(); // Reuses 0
        allocator.Allocate(); // New: 3
        Assert.Equal(4, allocator.UsedCount); // Now 4
    }
}
