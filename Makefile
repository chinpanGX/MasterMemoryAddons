# 機能ごとのコマンド(MasterMemory.Addons.Tools のサブコマンド)を、ここで呼ぶ。
# 自分のプロジェクトで使うときは、このファイルをコピーして CONFIG と TOOLS を書き換える。
#
#   make code-gen                コード生成。samples/masterdata.yaml の出力先に client / server 用のソースを置く
#   make code-gen ENV=dev        環境 dev の上書きを使う
#   make code-gen CONFIG=path    別の設定ファイルを使う
#   make master-gen              マスタ生成。CSV を検証してバイナリを作る(生成ソースが古ければ、コード生成もやり直す)
#   make master-gen-check        マスタ生成の検証だけを行い、何も書き込まない
#
# スプレッドシートのダウンロードは扱わない。CSV は CONFIG の csv のディレクトリに用意しておく(docs/design/pipeline.md)。

CONFIG ?= samples/masterdata.yaml
ENV ?=
TOOLS ?= dotnet run --project src/MasterMemory.Addons.Tools --

ENV_OPTION = $(if $(ENV),--env $(ENV))

.PHONY: help build test code-gen master-gen master-gen-check

# 表示は ASCII だけにする(Windows のコンソールでは日本語が文字化けするため)
help:
	@echo "make build"
	@echo "make test"
	@echo "make code-gen [CONFIG=<file>] [ENV=<name>]"
	@echo "make master-gen [CONFIG=<file>] [ENV=<name>]"
	@echo "make master-gen-check [CONFIG=<file>] [ENV=<name>]"

build:
	dotnet build MasterMemory.Addons.slnx

test:
	dotnet test MasterMemory.Addons.slnx

code-gen:
	$(TOOLS) code-gen --config $(CONFIG) $(ENV_OPTION)

master-gen:
	$(TOOLS) master-gen --config $(CONFIG) $(ENV_OPTION)

master-gen-check:
	$(TOOLS) master-gen --config $(CONFIG) $(ENV_OPTION) --check
