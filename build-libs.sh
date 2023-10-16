#!/bin/bash

LIBS_DIR="./libs"
TARGET_FRAMEWORK="net7.0"

for proj in $(find . -name "*.csproj"); do
    namespace=$(msbuild -property:Configuration=Release -nologo -v:q -t:PrintServiceLib $proj)

    if [[ ! -z "$namespace" ]]; then
        dotnet publish $proj -c Release -f $TARGET_FRAMEWORK
        publish_dir=$(dirname $proj)/bin/Release/$TARGET_FRAMEWORK/publish/
        mkdir -p $LIBS_DIR/$namespace
        cp $publish_dir/*.dll $LIBS_DIR/$namespace/
    fi
done
