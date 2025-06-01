# https://docs.docker.com/guides/bake/#exporting-build-artifacts

variable "VERSION" {
  default = "1.0.0"
}
variable "COMMIT" {
  default = "LOCAL"
}
variable "IMAGE" {
  default = "eddietisma/krp"
}

group "default" {
  targets = ["build", "output", "final"]
}

target "base" {
  context = "."
  dockerfile = "Dockerfile"
  cache-from = ["type=registry,ref=${IMAGE}:build"]
  args = {
    VERSION = "${VERSION}"
    COMMIT = "${COMMIT}"
  }
}

target "build" {
  inherits = ["base"]
  target = "build"
  # cache-to = ["type=registry,ref=krp:build,mode=max"] # requires the docker-container driver 
  output = ["type=docker,name=${IMAGE}:build"]
}

target "final" {
  inherits = ["base"]
  target = "final"
  contexts = {
    build = "target:build"
  }
  #tags = ["${IMAGE}:${VERSION}-${COMMIT}"]
  output = [
    #"type=tar,dest=krp-image-${VERSION}-${COMMIT}.tar",
    "type=docker,name=${IMAGE}:${VERSION}-${COMMIT}",
    #"type=docker,name=eddietisma/krp:latest" 
    #"type=tar,dest=krp.tar"
    #"type=image,name=krp:${VERSION}-${COMMIT},load=true"
  ]

}

target "output" {
  inherits = ["base"]
  depends_on  = ["final"]
  target = "output"
  contexts = {
    build = "target:build"
  }
  output = [
    "type=local,dest=output"
  ]
}

