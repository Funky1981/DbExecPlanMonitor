// Jenkins CI/CD Pipeline for DbExecPlanMonitor
// Builds, tests, and optionally publishes the application

pipeline {
    agent any

    environment {
        DOTNET_VERSION = '8.0'
        SOLUTION_PATH = 'src/DbExecPlanMonitor.sln'
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_NOLOGO = '1'
        // Docker registry settings - configure in Jenkins credentials
        DOCKER_REGISTRY = credentials('docker-registry-url')
        DOCKER_CREDENTIALS = credentials('docker-registry-credentials')
    }

    options {
        buildDiscarder(logRotator(numToKeepStr: '10'))
        timestamps()
        timeout(time: 30, unit: 'MINUTES')
        disableConcurrentBuilds()
    }

    tools {
        dotnetsdk 'dotnet-8.0'  // Configure in Jenkins Global Tool Configuration
    }

    stages {
        // ============================================
        // Checkout Stage
        // ============================================
        stage('Checkout') {
            steps {
                checkout scm
                script {
                    env.GIT_COMMIT_SHORT = sh(
                        script: 'git rev-parse --short HEAD',
                        returnStdout: true
                    ).trim()
                    env.GIT_TAG = sh(
                        script: 'git tag --points-at HEAD || echo ""',
                        returnStdout: true
                    ).trim()
                }
            }
        }

        // ============================================
        // Restore Dependencies
        // ============================================
        stage('Restore') {
            steps {
                sh "dotnet restore ${SOLUTION_PATH}"
            }
        }

        // ============================================
        // Build Stage
        // ============================================
        stage('Build') {
            steps {
                sh "dotnet build ${SOLUTION_PATH} --configuration Release --no-restore"
            }
        }

        // ============================================
        // Test Stage
        // ============================================
        stage('Test') {
            steps {
                sh """
                    dotnet test ${SOLUTION_PATH} \
                        --configuration Release \
                        --no-build \
                        --verbosity normal \
                        --collect:"XPlat Code Coverage" \
                        --results-directory ./TestResults \
                        --logger "trx;LogFileName=test-results.trx" \
                        --logger "junit;LogFileName=test-results.xml"
                """
            }
            post {
                always {
                    // Publish test results
                    junit testResults: '**/TestResults/*.xml', allowEmptyResults: true
                    
                    // Publish MSTest/TRX results
                    mstest testResultsFile: '**/TestResults/*.trx', failOnError: false
                    
                    // Publish code coverage (requires Cobertura plugin)
                    publishCoverage adapters: [
                        coberturaAdapter(path: '**/TestResults/**/coverage.cobertura.xml')
                    ], sourceFileResolver: sourceFiles('STORE_LAST_BUILD')
                }
            }
        }

        // ============================================
        // Code Analysis (Optional)
        // ============================================
        stage('Code Analysis') {
            when {
                anyOf {
                    branch 'master'
                    branch 'main'
                    changeRequest()
                }
            }
            steps {
                sh "dotnet format ${SOLUTION_PATH} --verify-no-changes --verbosity diagnostic || true"
            }
        }

        // ============================================
        // Build Docker Image
        // ============================================
        stage('Docker Build') {
            when {
                anyOf {
                    branch 'master'
                    branch 'main'
                    buildingTag()
                }
            }
            steps {
                script {
                    def imageTag = env.GIT_TAG ?: env.GIT_COMMIT_SHORT
                    
                    docker.withRegistry("https://${DOCKER_REGISTRY}", 'docker-registry-credentials') {
                        def image = docker.build("dbexecplanmonitor:${imageTag}", 
                            "--build-arg BUILD_DATE=\$(date -u +\"%Y-%m-%dT%H:%M:%SZ\") " +
                            "--build-arg VCS_REF=${env.GIT_COMMIT} ."
                        )
                        
                        image.push()
                        image.push(env.GIT_COMMIT_SHORT)
                        
                        if (env.BRANCH_NAME == 'master' || env.BRANCH_NAME == 'main') {
                            image.push('latest')
                        }
                    }
                }
            }
        }

        // ============================================
        // Publish Windows Artifacts
        // ============================================
        stage('Publish Windows') {
            when {
                buildingTag()
            }
            steps {
                sh """
                    dotnet publish src/DbExecPlanMonitor.Worker/DbExecPlanMonitor.Worker.csproj \
                        -c Release \
                        -r win-x64 \
                        --self-contained false \
                        -o ./publish/windows
                """
                sh """
                    cp scripts/Install-WindowsService.ps1 ./publish/windows/
                    cp scripts/Uninstall-WindowsService.ps1 ./publish/windows/
                """
                zip zipFile: 'DbExecPlanMonitor-windows-x64.zip', 
                    archive: true, 
                    dir: './publish/windows'
            }
        }

        // ============================================
        // Publish Linux Artifacts
        // ============================================
        stage('Publish Linux') {
            when {
                buildingTag()
            }
            steps {
                sh """
                    dotnet publish src/DbExecPlanMonitor.Worker/DbExecPlanMonitor.Worker.csproj \
                        -c Release \
                        -r linux-x64 \
                        --self-contained false \
                        -o ./publish/linux
                """
                sh """
                    cp scripts/install-linux-service.sh ./publish/linux/
                    cp scripts/dbexecplanmonitor.service ./publish/linux/
                    chmod +x ./publish/linux/install-linux-service.sh
                """
                sh 'tar -czvf DbExecPlanMonitor-linux-x64.tar.gz -C ./publish/linux .'
                archiveArtifacts artifacts: 'DbExecPlanMonitor-linux-x64.tar.gz', fingerprint: true
            }
        }

        // ============================================
        // Deploy to Staging
        // ============================================
        stage('Deploy to Staging') {
            when {
                anyOf {
                    branch 'master'
                    branch 'main'
                }
            }
            steps {
                script {
                    input message: 'Deploy to Staging?', ok: 'Deploy'
                }
                echo "Deploying ${env.GIT_COMMIT_SHORT} to staging environment..."
                // Add your staging deployment commands here
                // Example: sh 'kubectl set image deployment/dbexecplanmonitor app=${DOCKER_REGISTRY}/dbexecplanmonitor:${GIT_COMMIT_SHORT}'
            }
        }

        // ============================================
        // Deploy to Production
        // ============================================
        stage('Deploy to Production') {
            when {
                buildingTag()
            }
            steps {
                script {
                    input message: 'Deploy to Production?', ok: 'Deploy', submitter: 'admin,release-managers'
                }
                echo "Deploying ${env.GIT_TAG} to production environment..."
                // Add your production deployment commands here
            }
        }
    }

    post {
        always {
            cleanWs()
        }
        success {
            echo 'Pipeline completed successfully!'
            // Notify on success (configure as needed)
            // slackSend(color: 'good', message: "Build Successful: ${env.JOB_NAME} #${env.BUILD_NUMBER}")
        }
        failure {
            echo 'Pipeline failed!'
            // Notify on failure (configure as needed)
            // slackSend(color: 'danger', message: "Build Failed: ${env.JOB_NAME} #${env.BUILD_NUMBER}")
            // emailext(
            //     subject: "Build Failed: ${env.JOB_NAME} #${env.BUILD_NUMBER}",
            //     body: "Check console output at ${env.BUILD_URL}",
            //     recipientProviders: [[$class: 'DevelopersRecipientProvider']]
            // )
        }
        unstable {
            echo 'Pipeline is unstable (test failures).'
        }
    }
}
