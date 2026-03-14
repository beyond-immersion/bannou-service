#!/bin/bash

# run-tests.sh
# Comprehensive unit testing for service plugins
# Usage: run-tests.sh [plugin-name]
#        If plugin-name specified, only tests that plugin

PLUGIN="$1"

if [ -n "$PLUGIN" ]; then
    echo "🧪 Running unit tests for plugin: $PLUGIN..."

    test_project="./plugins/lib-$PLUGIN.tests/lib-$PLUGIN.tests.csproj"

    if [ -f "$test_project" ]; then
        echo "🧪 Running tests in: $test_project"
        dotnet test --project "$test_project" --verbosity minimal

        if [ $? -eq 0 ]; then
            echo "✅ Unit testing completed for plugin: $PLUGIN"
        else
            echo "❌ Unit testing failed for plugin: $PLUGIN"
            exit 1
        fi
    else
        echo "❌ Test project not found: $test_project"
        exit 1
    fi
else
    echo "🧪 Running comprehensive unit tests across all service plugins..."

    # Find all test projects
    test_projects=($(find . -name "*.tests.csproj" -o -name "*Tests.csproj" | grep -v template))

    if [ ${#test_projects[@]} -eq 0 ]; then
        echo "⚠️  No test projects found"
        exit 0
    fi

    failed_projects=()

    for test_project in "${test_projects[@]}"; do
        echo "🧪 Running tests in: $test_project"

        if ! dotnet test --project "$test_project" --verbosity minimal; then
            echo "⚠️  Tests failed in $test_project"
            failed_projects+=("$test_project")
        fi
    done

    echo ""
    if [ ${#failed_projects[@]} -eq 0 ]; then
        echo "✅ Comprehensive unit testing completed - all tests passed!"
    else
        echo "⚠️  Some tests failed:"
        for failed_project in "${failed_projects[@]}"; do
            echo "  ❌ $failed_project"
        done
        echo ""
        echo "💡 Run individual tests with: $0 [plugin-name]"
        exit 1
    fi
fi
