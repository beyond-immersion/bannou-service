#!/bin/bash

LIBS_DIR="libs"
TARGET_FRAMEWORK="net7.0"
rm -rf $LIBS_DIR
mkdir $LIBS_DIR

projects=($(find . -name '*.csproj'))
for proj in "${projects[@]}"
do
    namespace=$(msbuild -property:Configuration=Release -nologo -v:q -t:PrintServiceLib $proj)

    if [[ ! -z "$namespace" ]]; then
        target_dir="$LIBS_DIR/$namespace"
        mkdir -p "$target_dir"
        
        cp "$(dirname $proj)/bin/Release/$TARGET_FRAMEWORK/"* "$target_dir/"
        deps=$(msbuild.exe -property:Configuration=Release -nologo -v:q -t:PrintServiceLibDependencies $proj)

        if [[ ! -z "$deps" ]]; then
            IFS=',' read -ra ADDR <<< "$deps"
            for dep in "${ADDR[@]}"; do
                echo "$dep" >> "$target_dir/service_dependencies.txt"
            done
        fi
    fi
done
